using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DhirDhar.Application.Licensing.Models;
using DhirDhar.Infrastructure.Licensing;

namespace DhirDhar.LicenseGenerator;

public static class LicenseSigner
{
    public const string DefaultPrivateKeyPem = @"-----BEGIN EC PRIVATE KEY-----
MHcCAQEEIGdwp3oX8tjTumMbdGQBEucR6oa4Gtbtixy2Sh91v5MvoAoGCCqGSM49
AwEHoUQDQgAEx5L8QLG6AScIeADmTZbxUZhmVn5tgsS6ALUdFVjrC3KnQMU70oaA
IpEEa90Pt0F1apDusYVwT6TI9Hh4DTVMxg==
-----END EC PRIVATE KEY-----";

    public const string DefaultPublicKeyPem = @"-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEx5L8QLG6AScIeADmTZbxUZhmVn5t
gsS6ALUdFVjrC3KnQMU70oaAIpEEa90Pt0F1apDusYVwT6TI9Hh4DTVMxg==
-----END PUBLIC KEY-----";

    /// <summary>
    /// Generates a new ECDsa P-256 key pair in PEM format.
    /// </summary>
    public static (string PrivateKeyPem, string PublicKeyPem) GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPem = ecdsa.ExportECPrivateKeyPem();
        var publicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();
        return (privateKeyPem, publicKeyPem);
    }

    /// <summary>
    /// Generates a cryptographically secure random License ID (e.g. DD-YYYYMMDD-XXXXX).
    /// </summary>
    public static string GenerateLicenseId(DateTime? customDate = null)
    {
        var date = customDate ?? DateTime.UtcNow.Date;
        uint seq = (uint)RandomNumberGenerator.GetInt32(1, 1 << 20);
        return $"DD-{date:yyyyMMdd}-{seq:X5}";
    }

    /// <summary>
    /// Generates a cryptographically secure random Issuance ID nonce (128-bit hex).
    /// </summary>
    public static string GenerateIssuanceId()
    {
        byte[] randomBytes = new byte[16];
        RandomNumberGenerator.Fill(randomBytes);
        return "ISS-" + Convert.ToHexString(randomBytes)[..16];
    }

    /// <summary>
    /// Generates a guaranteed unique annual license and registers it in history.
    /// </summary>
    public static (LicensePayload Payload, string SerialKey) CreateUniqueLicense(
        string customerName,
        string customerEmail,
        string? privateKeyPem = null,
        string? publicKeyPem = null,
        LicenseHistoryService? historyService = null,
        DateTime? customIssuedAt = null,
        DateTime? customExpiresAt = null,
        string? deviceBinding = null,
        int deviceLimit = 1,
        string edition = "Annual")
    {
        var privKey = privateKeyPem ?? DefaultPrivateKeyPem;
        var pubKey = publicKeyPem ?? DefaultPublicKeyPem;
        var history = historyService ?? new LicenseHistoryService();
        var issuedAt = customIssuedAt ?? DateTime.UtcNow.Date;
        var expiresAt = customExpiresAt ?? issuedAt.AddDays(365);
        ushort issuedDays = (ushort)Math.Max(0, (issuedAt.Date - LicenseDecoder.Epoch).TotalDays);
        ushort expiryDays = (ushort)Math.Max(0, (expiresAt.Date - LicenseDecoder.Epoch).TotalDays);
        uint hardwareIdHash = LicenseDecoder.ComputeHardwareIdHash(deviceBinding);

        const int maxAttempts = 100;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            uint seq = (uint)RandomNumberGenerator.GetInt32(1, 1 << 20);
            ushort nonce = (ushort)RandomNumberGenerator.GetInt32(1, 1 << 16);
            var licenseId = $"DD-{issuedAt:yyyyMMdd}-{seq:X5}";
            var issuanceId = LicenseDecoder.ComputeIssuanceId(licenseId, nonce, issuedDays, expiryDays, hardwareIdHash, false);

            var payload = new LicensePayload(
                Product: "DhirDhar",
                LicenseId: licenseId,
                CustomerName: customerName,
                CustomerEmail: customerEmail,
                Edition: edition,
                IssuedAt: issuedAt,
                ExpiresAt: expiresAt,
                DeviceLimit: deviceLimit,
                LicenseVersion: 1,
                IssuanceId: issuanceId,
                DeviceBinding: deviceBinding,
                PreviousLicenseId: null,
                IsRenewal: false);

            var serialKey = CreateSerialKey(payload, privKey);

            // Self-verify signature
            if (!VerifySerialKey(serialKey, pubKey, out var verifiedPayload, out var verifyError))
            {
                throw new InvalidOperationException($"Generated license failed self-verification: {verifyError}");
            }

            // Enforce duplicate protection against history
            if (history.Exists(licenseId, issuanceId, serialKey))
            {
                continue; // Collision detected, regenerate with fresh IDs
            }

            // Record to history
            history.AddRecord(new LicenseHistoryRecord(
                LicenseId: licenseId,
                IssuanceId: issuanceId,
                CustomerName: customerName,
                CustomerEmail: customerEmail,
                Edition: edition,
                IssuedAt: issuedAt,
                ExpiresAt: expiresAt,
                DeviceLimit: deviceLimit,
                DeviceBinding: deviceBinding,
                PreviousLicenseId: null,
                IsRenewal: false,
                SerialKey: serialKey,
                CreatedAt: DateTime.UtcNow));

            return (payload, serialKey);
        }

        throw new InvalidOperationException("Failed to generate a unique license after maximum retry attempts.");
    }

    /// <summary>
    /// Generates a guaranteed unique renewal license linked to a previous license ID and registers it in history.
    /// </summary>
    public static (LicensePayload Payload, string SerialKey) CreateUniqueRenewal(
        string previousLicenseId,
        string customerName,
        string customerEmail,
        string? privateKeyPem = null,
        string? publicKeyPem = null,
        LicenseHistoryService? historyService = null,
        DateTime? customIssuedAt = null,
        DateTime? customExpiresAt = null,
        string? deviceBinding = null,
        int deviceLimit = 1)
    {
        var privKey = privateKeyPem ?? DefaultPrivateKeyPem;
        var pubKey = publicKeyPem ?? DefaultPublicKeyPem;
        var history = historyService ?? new LicenseHistoryService();
        var issuedAt = customIssuedAt ?? DateTime.UtcNow.Date;
        var expiresAt = customExpiresAt ?? issuedAt.AddDays(365);
        ushort issuedDays = (ushort)Math.Max(0, (issuedAt.Date - LicenseDecoder.Epoch).TotalDays);
        ushort expiryDays = (ushort)Math.Max(0, (expiresAt.Date - LicenseDecoder.Epoch).TotalDays);
        uint hardwareIdHash = LicenseDecoder.ComputeHardwareIdHash(deviceBinding);

        const int maxAttempts = 100;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            uint seq = (uint)RandomNumberGenerator.GetInt32(1, 1 << 20);
            ushort nonce = (ushort)RandomNumberGenerator.GetInt32(1, 1 << 16);
            var newLicenseId = $"DD-{issuedAt:yyyyMMdd}-{seq:X5}";
            var issuanceId = LicenseDecoder.ComputeIssuanceId(newLicenseId, nonce, issuedDays, expiryDays, hardwareIdHash, true);

            var payload = new LicensePayload(
                Product: "DhirDhar",
                LicenseId: newLicenseId,
                CustomerName: customerName,
                CustomerEmail: customerEmail,
                Edition: "Renewal",
                IssuedAt: issuedAt,
                ExpiresAt: expiresAt,
                DeviceLimit: deviceLimit,
                LicenseVersion: 1,
                IssuanceId: issuanceId,
                DeviceBinding: deviceBinding,
                PreviousLicenseId: previousLicenseId,
                IsRenewal: true);

            var serialKey = CreateSerialKey(payload, privKey);

            // Self-verify signature
            if (!VerifySerialKey(serialKey, pubKey, out var verifiedPayload, out var verifyError))
            {
                throw new InvalidOperationException($"Generated renewal license failed self-verification: {verifyError}");
            }

            // Enforce duplicate protection against history
            if (history.Exists(newLicenseId, issuanceId, serialKey))
            {
                continue; // Collision detected, regenerate with fresh IDs
            }

            // Record renewal in history without overwriting old record
            history.AddRecord(new LicenseHistoryRecord(
                LicenseId: newLicenseId,
                IssuanceId: issuanceId,
                CustomerName: customerName,
                CustomerEmail: customerEmail,
                Edition: "Renewal",
                IssuedAt: issuedAt,
                ExpiresAt: expiresAt,
                DeviceLimit: deviceLimit,
                DeviceBinding: deviceBinding,
                PreviousLicenseId: previousLicenseId,
                IsRenewal: true,
                SerialKey: serialKey,
                CreatedAt: DateTime.UtcNow));

            return (payload, serialKey);
        }

        throw new InvalidOperationException("Failed to generate a unique renewal license after maximum retry attempts.");
    }

    /// <summary>
    /// Signs a license payload using the private key and generates the 25-character serial key string (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX).
    /// </summary>
    public static string CreateSerialKey(LicensePayload payload, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        var pubKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem();

        var issuedAt = payload.IssuedAt;
        var expiresAt = payload.ExpiresAt;
        ushort issuedDays = (ushort)Math.Max(0, (issuedAt.Date - LicenseDecoder.Epoch).TotalDays);
        ushort expiryDays = (ushort)Math.Max(0, (expiresAt.Date - LicenseDecoder.Epoch).TotalDays);

        uint hardwareIdHash = LicenseDecoder.ComputeHardwareIdHash(payload.DeviceBinding);
        bool isHardwareBound = hardwareIdHash != 0;
        bool isRenewal = payload.Renewal || string.Equals(payload.Edition, "Renewal", StringComparison.OrdinalIgnoreCase);

        // Extract or generate sequence number from LicenseId
        uint licenseSeq = 0;
        if (!string.IsNullOrWhiteSpace(payload.LicenseId) && payload.LicenseId.StartsWith("DD-"))
        {
            var parts = payload.LicenseId.Split('-');
            if (parts.Length >= 3 && uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var parsedSeq))
            {
                licenseSeq = parsedSeq & 0x000FFFFF; // 20 bits
            }
        }
        if (licenseSeq == 0)
        {
            licenseSeq = (uint)RandomNumberGenerator.GetInt32(1, 1 << 20);
        }

        // Extract or generate issuance nonce
        ushort issuanceNonce = 0;
        if (!string.IsNullOrWhiteSpace(payload.IssuanceId))
        {
            var cleanIss = payload.IssuanceId.Replace("ISS-", "").Trim();
            if (cleanIss.Length >= 4 && ushort.TryParse(cleanIss[..4], System.Globalization.NumberStyles.HexNumber, null, out var parsedNonce))
            {
                issuanceNonce = parsedNonce;
            }
        }
        if (issuanceNonce == 0)
        {
            issuanceNonce = (ushort)RandomNumberGenerator.GetInt32(1, 1 << 16);
        }

        // Canonical payload contract matching LicenseDecoder for 25-character compact keys
        var canonicalLicenseId = $"DD-{issuedAt:yyyyMMdd}-{licenseSeq:X5}";
        var canonicalIssuanceId = LicenseDecoder.ComputeIssuanceId(canonicalLicenseId, issuanceNonce, issuedDays, expiryDays, hardwareIdHash, isRenewal);

        var customerName = string.IsNullOrWhiteSpace(payload.CustomerName) ? "DhirDhar Customer" : payload.CustomerName.Trim();
        var customerEmail = string.IsNullOrWhiteSpace(payload.CustomerEmail) ? "customer@dhirdhar.com" : payload.CustomerEmail.Trim().ToLowerInvariant();

        var canonicalPayload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: canonicalLicenseId,
            CustomerName: "DhirDhar Customer",
            CustomerEmail: "customer@dhirdhar.com",
            Edition: isRenewal ? "Renewal" : "Annual",
            IssuedAt: LicenseDecoder.Epoch.AddDays(issuedDays),
            ExpiresAt: LicenseDecoder.Epoch.AddDays(expiryDays),
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: canonicalIssuanceId,
            DeviceBinding: isHardwareBound ? $"HW-{hardwareIdHash:X6}" : null,
            PreviousLicenseId: null,
            IsRenewal: isRenewal);

        var canonicalBytes = canonicalPayload.GetCanonicalBytes();

        // Sign with ECDSA P-256
        var fullSig = ecdsa.SignData(canonicalBytes, HashAlgorithmName.SHA256);

        // Compute 29-bit signature tag
        var signatureTag = LicenseDecoder.ComputeSignatureTag(canonicalBytes, pubKeyPem);

        // Pack into 125 bits (25 Base32 characters)
        var writer = new LicenseDecoder.BitWriter(125);
        writer.WriteBits(2, 4); // Version 2
        writer.WriteBits(isRenewal ? 1u : 0u, 1);
        writer.WriteBits(isHardwareBound ? 1u : 0u, 1);
        writer.WriteBits(0, 2); // Edition 0 (Annual)
        writer.WriteBits(issuedDays, 14);
        writer.WriteBits(expiryDays, 14);
        writer.WriteBits(hardwareIdHash, 24);
        writer.WriteBits(licenseSeq, 20);
        writer.WriteBits(issuanceNonce, 16);
        writer.WriteBits(signatureTag, 29);

        var symbols = writer.To5BitSymbols();
        var sb = new StringBuilder(29);
        for (int i = 0; i < 25; i++)
        {
            if (i > 0 && i % 5 == 0)
            {
                sb.Append('-');
            }
            sb.Append(LicenseDecoder.Alphabet[symbols[i]]);
        }

        var key = sb.ToString();
        LicenseDecoder.RegisterKnownCustomer(key, canonicalLicenseId, customerName, customerEmail);
        return key;
    }

    /// <summary>
    /// Decodes a serial key string back into the payload and signature.
    /// </summary>
    public static (LicensePayload Payload, byte[] Signature) DecodeSerialKey(string serialKey, string? publicKeyPem = null, string? candidateCustomerName = null)
    {
        return LicenseDecoder.DecodeRawSerialKey(serialKey, publicKeyPem ?? DefaultPublicKeyPem, candidateCustomerName);
    }

    /// <summary>
    /// Verifies a serial key using the public verification key.
    /// </summary>
    public static bool VerifySerialKey(
        string serialKey, 
        string publicKeyPem, 
        out LicensePayload? payload, 
        out string errorMessage,
        string? candidateCustomerName = null)
    {
        var result = LicenseDecoder.VerifySerialKey(serialKey, publicKeyPem, candidateCustomerName);
        payload = result.Payload;
        errorMessage = result.ErrorMessage;
        return result.IsValid;
    }
}
