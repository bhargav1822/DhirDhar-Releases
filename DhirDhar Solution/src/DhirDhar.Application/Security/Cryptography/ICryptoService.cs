using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Security.Models;

namespace DhirDhar.Application.Security.Cryptography;

public interface ICryptoService
{
    byte[] GenerateRandomKey(int sizeInBytes = 32);

    byte[] GenerateRandomNonce(int sizeInBytes = 12);

    byte[] DeriveKey(byte[] masterKey, string purpose, int sizeInBytes = 32);

    byte[] DeriveKeyFromPassphrase(string passphrase, byte[] salt, int iterations = 600_000, int sizeInBytes = 32);

    byte[] DeriveKeyFromRecoveryKey(string recoveryKey, byte[] salt, int iterations = 600_000, int sizeInBytes = 32);

    byte[] DerivePortableBackupKey(string? passwordOrRecoveryKey, byte[] salt, int iterations = 600_000, int sizeInBytes = 32);

    EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, byte[] key, ReadOnlySpan<byte> associatedData = default);

    byte[] Decrypt(EncryptedPayload payload, byte[] key, ReadOnlySpan<byte> associatedData = default);

    string EncryptString(string plaintext, byte[] key);

    string DecryptString(string base64Payload, byte[] key);

    string ComputeBlindIndex(string input, byte[] blindIndexKey);

    Task EncryptStreamAsync(Stream inputStream, Stream outputStream, byte[] key, CancellationToken cancellationToken = default);

    Task DecryptStreamAsync(Stream inputStream, Stream outputStream, byte[] key, CancellationToken cancellationToken = default);
}
