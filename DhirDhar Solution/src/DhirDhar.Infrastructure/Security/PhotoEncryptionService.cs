using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Security;
using DhirDhar.Application.Security.Cryptography;
using DhirDhar.Application.Security.Keys;
using DhirDhar.Application.Security.Models;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Security;

public sealed class PhotoEncryptionService : IPhotoEncryptionService
{
    private readonly ICryptoService _cryptoService;
    private readonly IKeyManagementService _keyManagementService;
    private readonly ILogger<PhotoEncryptionService> _logger;

    public PhotoEncryptionService(
        ICryptoService cryptoService,
        IKeyManagementService keyManagementService,
        ILogger<PhotoEncryptionService> logger)
    {
        _cryptoService = cryptoService;
        _keyManagementService = keyManagementService;
        _logger = logger;
    }

    private string GetPhotosDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "DhirDhar", "Photos");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> EncryptAndStorePhotoAsync(string sourcePlaintextFilePath, string photoCategory = "borrower", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePlaintextFilePath) || !File.Exists(sourcePlaintextFilePath))
        {
            throw new FileNotFoundException("Source photo file not found.", sourcePlaintextFilePath);
        }

        var photoKey = _keyManagementService.GetPhotoEncryptionKey();
        var photosDir = GetPhotosDirectory();
        var encryptedFileName = $"{photoCategory}_{Guid.NewGuid():N}.ddenc";
        var targetEncryptedPath = Path.Combine(photosDir, encryptedFileName);

        var plainBytes = await File.ReadAllBytesAsync(sourcePlaintextFilePath, cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = _cryptoService.Encrypt(plainBytes, photoKey);
            await File.WriteAllBytesAsync(targetEncryptedPath, payload.ToBytes(), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Photo securely encrypted with AES-256-GCM: {Path}", targetEncryptedPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }

        // Securely delete temporary plaintext photo
        try
        {
            var len = new FileInfo(sourcePlaintextFilePath).Length;
            var zeros = new byte[Math.Min(len, 64 * 1024)];
            using (var fs = new FileStream(sourcePlaintextFilePath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var remaining = len;
                while (remaining > 0)
                {
                    var writeLen = (int)Math.Min(remaining, zeros.Length);
                    fs.Write(zeros, 0, writeLen);
                    remaining -= writeLen;
                }
            }
            File.Delete(sourcePlaintextFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to securely overwrite temporary plaintext photo: {Path}", sourcePlaintextFilePath);
            try { File.Delete(sourcePlaintextFilePath); } catch { }
        }

        return targetEncryptedPath;
    }

    public async Task<Stream> DecryptPhotoToStreamAsync(string encryptedPhotoPath, CancellationToken cancellationToken = default)
    {
        var bytes = await DecryptPhotoToBytesAsync(encryptedPhotoPath, cancellationToken).ConfigureAwait(false);
        return new MemoryStream(bytes);
    }

    public async Task<byte[]> DecryptPhotoToBytesAsync(string encryptedPhotoPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(encryptedPhotoPath) || !File.Exists(encryptedPhotoPath))
        {
            throw new FileNotFoundException("Encrypted photo file not found.", encryptedPhotoPath);
        }

        var photoKey = _keyManagementService.GetPhotoEncryptionKey();
        var encryptedBytes = await File.ReadAllBytesAsync(encryptedPhotoPath, cancellationToken).ConfigureAwait(false);

        if (encryptedBytes.Length >= 4 && encryptedBytes.AsSpan(0, 4).SequenceEqual(EncryptedPayload.MagicBytes))
        {
            var payload = EncryptedPayload.FromBytes(encryptedBytes);
            return _cryptoService.Decrypt(payload, photoKey);
        }

        // Return raw bytes if file is a legacy unencrypted photo
        return encryptedBytes;
    }

    public bool IsPhotoEncrypted(string photoPath)
    {
        if (string.IsNullOrWhiteSpace(photoPath) || !File.Exists(photoPath))
        {
            return false;
        }

        if (photoPath.EndsWith(".ddenc", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var fs = new FileStream(photoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[4];
            if (fs.Read(header, 0, 4) == 4)
            {
                return header.AsSpan().SequenceEqual(EncryptedPayload.MagicBytes);
            }
        }
        catch
        {
        }

        return false;
    }
}
