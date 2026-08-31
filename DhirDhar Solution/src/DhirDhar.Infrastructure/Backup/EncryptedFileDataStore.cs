using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Backup;

public sealed class EncryptedFileDataStore : IDataStore
{
    private readonly string _folderPath;
    private readonly ILogger? _logger;

    public EncryptedFileDataStore(string folderPath, ILogger? logger = null)
    {
        _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        _logger = logger;
        Directory.CreateDirectory(_folderPath);
    }

    public Task StoreAsync<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));
        }

        try
        {
            var filePath = GetFilePath(key);
            var json = JsonSerializer.Serialize(value);
            var rawBytes = Encoding.UTF8.GetBytes(json);

            byte[] encryptedBytes;
            if (OperatingSystem.IsWindows())
            {
                encryptedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);
            }
            else
            {
                encryptedBytes = rawBytes;
            }

            File.WriteAllBytes(filePath, encryptedBytes);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to securely store token for key {Key}", key);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return Task.FromResult<T?>(default);
        }

        var filePath = GetFilePath(key);
        if (!File.Exists(filePath))
        {
            return Task.FromResult<T?>(default);
        }

        try
        {
            var fileBytes = File.ReadAllBytes(filePath);
            if (fileBytes.Length == 0)
            {
                return Task.FromResult<T?>(default);
            }

            byte[] rawBytes;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    rawBytes = ProtectedData.Unprotect(fileBytes, null, DataProtectionScope.CurrentUser);
                }
                catch (CryptographicException)
                {
                    // Token file was written unencrypted or encrypted under different user context.
                    // Fall back to rawBytes if valid JSON, otherwise purge file.
                    try
                    {
                        var plainTextTest = Encoding.UTF8.GetString(fileBytes);
                        var testObj = JsonSerializer.Deserialize<T>(plainTextTest);
                        if (testObj != null)
                        {
                            // Re-encrypt immediately in DPAPI
                            _ = StoreAsync(key, testObj);
                            return Task.FromResult<T?>(testObj);
                        }
                    }
                    catch
                    {
                    }

                    _logger?.LogWarning("Failed to decrypt stored token for key {Key}. Purging token file.", key);
                    File.Delete(filePath);
                    return Task.FromResult<T?>(default);
                }
            }
            else
            {
                rawBytes = fileBytes;
            }

            var json = Encoding.UTF8.GetString(rawBytes);
            var value = JsonSerializer.Deserialize<T>(json);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to retrieve stored token for key {Key}", key);
            return Task.FromResult<T?>(default);
        }
    }

    public Task DeleteAsync<T>(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return Task.CompletedTask;
        }

        try
        {
            var filePath = GetFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to delete stored token for key {Key}", key);
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        try
        {
            if (Directory.Exists(_folderPath))
            {
                foreach (var file in Directory.EnumerateFiles(_folderPath))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clear token store at {Path}", _folderPath);
        }

        return Task.CompletedTask;
    }

    private string GetFilePath(string key)
    {
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_folderPath, $"EncryptedDataStore_{safeKey}");
    }
}
