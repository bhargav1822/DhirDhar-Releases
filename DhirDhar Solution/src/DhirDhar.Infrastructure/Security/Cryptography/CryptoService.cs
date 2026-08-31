using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Security.Cryptography;
using DhirDhar.Application.Security.Models;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Security.Cryptography;

public sealed class CryptoService : ICryptoService
{
    private const int KeySize = 32; // 256 bits
    private const int NonceSize = 12; // 96 bits
    private const int TagSize = 16; // 128 bits
    private readonly ILogger<CryptoService> _logger;

    public CryptoService(ILogger<CryptoService> logger)
    {
        _logger = logger;
    }

    public byte[] GenerateRandomKey(int sizeInBytes = KeySize)
    {
        return RandomNumberGenerator.GetBytes(sizeInBytes);
    }

    public byte[] GenerateRandomNonce(int sizeInBytes = NonceSize)
    {
        return RandomNumberGenerator.GetBytes(sizeInBytes);
    }

    public byte[] DeriveKey(byte[] masterKey, string purpose, int sizeInBytes = KeySize)
    {
        if (masterKey == null || masterKey.Length == 0)
        {
            throw new ArgumentException("Master key cannot be null or empty.", nameof(masterKey));
        }

        var info = Encoding.UTF8.GetBytes(purpose);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, sizeInBytes, info: info);
    }

    public byte[] DeriveKeyFromPassphrase(string passphrase, byte[] salt, int iterations = 600_000, int sizeInBytes = KeySize)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new ArgumentException("Passphrase cannot be null or empty.", nameof(passphrase));
        }

        if (salt == null || salt.Length < 16)
        {
            throw new ArgumentException("Salt must be at least 16 bytes.", nameof(salt));
        }

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            sizeInBytes);
    }

    public byte[] DeriveKeyFromRecoveryKey(string recoveryKey, byte[] salt, int iterations = 600_000, int sizeInBytes = KeySize)
    {
        if (string.IsNullOrWhiteSpace(recoveryKey))
        {
            throw new ArgumentException("Recovery key cannot be null or empty.", nameof(recoveryKey));
        }

        if (salt == null || salt.Length < 16)
        {
            throw new ArgumentException("Salt must be at least 16 bytes.", nameof(salt));
        }

        var cleanHex = recoveryKey.Replace("DDRK-", "", StringComparison.OrdinalIgnoreCase).Replace("-", "").Trim();
        try
        {
            if (cleanHex.Length == 64)
            {
                var rawRecoveryBytes = Convert.FromHexString(cleanHex);
                return HKDF.DeriveKey(HashAlgorithmName.SHA256, rawRecoveryBytes, sizeInBytes, salt: salt, info: "DhirDhar-Portable-RecoveryKey-v3"u8.ToArray());
            }
        }
        catch
        {
            // Fall back to passphrase derivation if hex conversion fails
        }

        return DeriveKeyFromPassphrase(recoveryKey.Trim(), salt, iterations, sizeInBytes);
    }

    public byte[] DerivePortableBackupKey(string? passwordOrRecoveryKey, byte[] salt, int iterations = 600_000, int sizeInBytes = KeySize)
    {
        if (salt == null || salt.Length < 16)
        {
            throw new ArgumentException("Salt must be at least 16 bytes.", nameof(salt));
        }

        if (!string.IsNullOrWhiteSpace(passwordOrRecoveryKey))
        {
            var trimmed = passwordOrRecoveryKey.Trim();
            if (trimmed.StartsWith("DDRK-", StringComparison.OrdinalIgnoreCase) || (trimmed.Length == 64 && trimmed.All(Uri.IsHexDigit)))
            {
                return DeriveKeyFromRecoveryKey(trimmed, salt, iterations, sizeInBytes);
            }
            return DeriveKeyFromPassphrase(trimmed, salt, iterations, sizeInBytes);
        }

        // Standard portable application key derivation (per-backup unique salt ensures cryptographically distinct keys)
        return Rfc2898DeriveBytes.Pbkdf2(
            "DhirDhar.Standard.Portable.Backup.Key.v3"u8.ToArray(),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            sizeInBytes);
    }

    public EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, byte[] key, ReadOnlySpan<byte> associatedData = default)
    {
        if (key == null || key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be exactly {KeySize} bytes for AES-256.", nameof(key));
        }

        var nonce = GenerateRandomNonce(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        return new EncryptedPayload
        {
            Version = EncryptedPayload.CurrentVersion,
            Nonce = nonce,
            Tag = tag,
            Ciphertext = ciphertext
        };
    }

    public byte[] Decrypt(EncryptedPayload payload, byte[] key, ReadOnlySpan<byte> associatedData = default)
    {
        if (payload == null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (key == null || key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be exactly {KeySize} bytes for AES-256.", nameof(key));
        }

        var plaintext = new byte[payload.Ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext, associatedData);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            _logger.LogWarning("Authentication failed during decryption: Tag mismatch or corrupted ciphertext.");
            throw new CryptographicException("Decryption authentication failed. The data may have been tampered with or the wrong key was provided.", ex);
        }
    }

    public string EncryptString(string plaintext, byte[] key)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var payload = Encrypt(plainBytes, key);
            var bytes = payload.ToBytes();
            return Convert.ToBase64String(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public string DecryptString(string base64Payload, byte[] key)
    {
        if (string.IsNullOrEmpty(base64Payload))
        {
            return string.Empty;
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = Convert.FromBase64String(base64Payload);
        }
        catch
        {
            // Not a valid base64 payload - return raw string for graceful legacy compatibility
            return base64Payload;
        }

        if (payloadBytes.Length < 4 || !payloadBytes.AsSpan(0, 4).SequenceEqual(EncryptedPayload.MagicBytes))
        {
            // Plaintext legacy string
            return base64Payload;
        }

        var payload = EncryptedPayload.FromBytes(payloadBytes);
        var decryptedBytes = Decrypt(payload, key);
        try
        {
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decryptedBytes);
        }
    }

    public string ComputeBlindIndex(string input, byte[] blindIndexKey)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = input.Trim().ToLowerInvariant();
        var inputBytes = Encoding.UTF8.GetBytes(normalized);

        using var hmac = new HMACSHA256(blindIndexKey);
        var hash = hmac.ComputeHash(inputBytes);
        return Convert.ToHexString(hash);
    }

    public async Task EncryptStreamAsync(Stream inputStream, Stream outputStream, byte[] key, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await inputStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var plaintext = ms.ToArray();

        try
        {
            var payload = Encrypt(plaintext, key);
            var payloadBytes = payload.ToBytes();
            await outputStream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task DecryptStreamAsync(Stream inputStream, Stream outputStream, byte[] key, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await inputStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var payloadBytes = ms.ToArray();

        var payload = EncryptedPayload.FromBytes(payloadBytes);
        var plaintext = Decrypt(payload, key);

        try
        {
            await outputStream.WriteAsync(plaintext, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
