using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Licensing;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Licensing;

[SupportedOSPlatform("windows")]
public sealed class LicenseStorageService : ILicenseStorageService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DhirDhar::Annual::Offline::License::Storage::V1");
    private readonly string _storageFilePath;
    private readonly ILogger<LicenseStorageService>? _logger;

    public LicenseStorageService(ILogger<LicenseStorageService>? logger = null, string? customStoragePath = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(customStoragePath))
        {
            _storageFilePath = customStoragePath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var licenseDir = Path.Combine(localAppData, "DhirDhar", "License");
            _storageFilePath = Path.Combine(licenseDir, "activation.dat");
        }
    }

    public async Task<StoredActivation?> LoadActivationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_storageFilePath))
            {
                return null;
            }

            var encryptedBytes = await File.ReadAllBytesAsync(_storageFilePath, cancellationToken).ConfigureAwait(false);
            if (encryptedBytes.Length == 0)
            {
                return null;
            }

            byte[] decryptedBytes;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                decryptedBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                decryptedBytes = encryptedBytes;
            }

            var activation = JsonSerializer.Deserialize<StoredActivation>(decryptedBytes);
            if (activation is null)
            {
                return null;
            }

            // Verify internal integrity checksum
            var expectedChecksum = ComputeChecksum(activation.SerialKey, activation.BoundDeviceId, activation.ActivatedAt);
            if (!string.Equals(activation.Checksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogWarning("License storage integrity checksum mismatch.");
                return null;
            }

            return activation;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load stored license activation.");
            return null;
        }
    }

    public async Task SaveActivationAsync(StoredActivation activation, CancellationToken cancellationToken = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_storageFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Recompute checksum before saving
            var checksum = ComputeChecksum(activation.SerialKey, activation.BoundDeviceId, activation.ActivatedAt);
            var recordToSave = activation with { Checksum = checksum };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(recordToSave);

            byte[] bytesToWrite;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                bytesToWrite = ProtectedData.Protect(jsonBytes, Entropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                bytesToWrite = jsonBytes;
            }

            await File.WriteAllBytesAsync(_storageFilePath, bytesToWrite, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("License activation record saved successfully.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save license activation record.");
            throw;
        }
    }

    public Task ClearActivationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_storageFilePath))
            {
                File.Delete(_storageFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clear license activation record.");
        }

        return Task.CompletedTask;
    }

    public static string ComputeChecksum(string serialKey, string boundDeviceId, DateTime activatedAt)
    {
        var raw = $"{serialKey.Trim()}::{boundDeviceId.Trim()}::{activatedAt:yyyy-MM-ddTHH:mm:ssZ}";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
