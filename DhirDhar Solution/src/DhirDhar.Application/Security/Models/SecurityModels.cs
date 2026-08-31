using System;
using System.Collections.Generic;
using System.IO;

namespace DhirDhar.Application.Security.Models;

public sealed record EncryptionStatusInfo(
    bool IsEncryptionActive,
    bool IsDatabaseEncrypted,
    bool IsBackupEncrypted,
    bool IsKeyStorageSecure,
    string Algorithm,
    string EncryptionVersion,
    DateTime? LastVerifiedAt,
    bool HasUserPassphrase);

public sealed class EncryptedPayload
{
    public const byte CurrentVersion = 1;
    public static readonly byte[] MagicBytes = "DDE1"u8.ToArray();

    public byte Version { get; init; } = CurrentVersion;
    public byte[] Nonce { get; init; } = Array.Empty<byte>();
    public byte[] Tag { get; init; } = Array.Empty<byte>();
    public byte[] Ciphertext { get; init; } = Array.Empty<byte>();

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(MagicBytes);
        writer.Write(Version);
        writer.Write((byte)Nonce.Length);
        writer.Write(Nonce);
        writer.Write((byte)Tag.Length);
        writer.Write(Tag);
        writer.Write(Ciphertext.Length);
        writer.Write(Ciphertext);

        return ms.ToArray();
    }

    public static EncryptedPayload FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 + 1 + 1 + 12 + 1 + 16 + 4)
        {
            throw new ArgumentException("Invalid encrypted payload length.", nameof(data));
        }

        if (!data[..4].SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Invalid magic header for encrypted payload.");
        }

        var version = data[4];
        var nonceLen = data[5];
        var nonce = data.Slice(6, nonceLen).ToArray();

        var tagOffset = 6 + nonceLen;
        var tagLen = data[tagOffset];
        var tag = data.Slice(tagOffset + 1, tagLen).ToArray();

        var cipherLenOffset = tagOffset + 1 + tagLen;
        var cipherLen = BitConverter.ToInt32(data.Slice(cipherLenOffset, 4));

        var cipherOffset = cipherLenOffset + 4;
        var ciphertext = data.Slice(cipherOffset, cipherLen).ToArray();

        return new EncryptedPayload
        {
            Version = version,
            Nonce = nonce,
            Tag = tag,
            Ciphertext = ciphertext
        };
    }
}

public sealed record RecoveryKeyDetails(
    string FormattedRecoveryKey,
    DateTime CreatedAt);

public sealed record MigrationResult(
    bool IsSuccess,
    int BorrowersMigrated,
    int TransactionsMigrated,
    int PhotosMigrated,
    string? BackupPath,
    IReadOnlyList<string> Errors);
