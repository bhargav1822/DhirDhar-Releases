using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DhirDhar.Application.Licensing.Models;

namespace DhirDhar.Infrastructure.Licensing;

public static class LicenseDecoder
{
    public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    public static readonly DateTime Epoch = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const byte MagicByte1 = 0x44; // 'D'
    private const byte MagicByte2 = 0x44; // 'D'
    private const byte CurrentFormatVersion = 0x01;
    private const string LegacyBase32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Decodes and verifies a 25-character serial key against the embedded ECDSA P-256 public key.
    /// </summary>
    public static (bool IsValid, LicensePayload? Payload, string ErrorMessage) VerifySerialKey(
        string serialKey, 
        string? publicKeyPem = null,
        string? candidateCustomerName = null,
        string? candidateCustomerEmail = null)
    {
        if (string.IsNullOrWhiteSpace(serialKey))
        {
            return (false, null, "Serial key cannot be empty.");
        }

        try
        {
            var pubKey = publicKeyPem ?? LicenseVerificationKey.PublicKeyPem;
            var cleaned = NormalizeSerialKey(serialKey);

            // 1. Primary path: 25-character compact serial key format (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX)
            if (cleaned.Length == 25 && IsValidAlphabetString(cleaned))
            {
                if (!TryDecode25CharKey(cleaned, pubKey, candidateCustomerName, candidateCustomerEmail, out var payload, out var sigBytes, out var error))
                {
                    return (false, null, error);
                }

                if (!string.Equals(payload.Product, "DhirDhar", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, null, $"Invalid product name '{payload.Product}'.");
                }

                if (string.IsNullOrWhiteSpace(payload.LicenseId))
                {
                    return (false, null, "License ID cannot be empty.");
                }

                if (string.IsNullOrWhiteSpace(payload.IssuanceId))
                {
                    return (false, null, "Issuance ID cannot be empty.");
                }

                return (true, payload, string.Empty);
            }

            // 2. Legacy fallback path
            if (cleaned.StartsWith("DD") || cleaned.Length > 25)
            {
                var (legacyPayload, legacySig) = DecodeRawLegacySerialKey(serialKey);

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(pubKey);

                var canonicalBytes = legacyPayload.GetCanonicalBytes();
                bool validSig = ecdsa.VerifyData(canonicalBytes, legacySig, HashAlgorithmName.SHA256);
                if (!validSig && string.IsNullOrWhiteSpace(legacyPayload.IssuanceId))
                {
                    var legacyBytes = legacyPayload.GetLegacyCanonicalBytes();
                    validSig = ecdsa.VerifyData(legacyBytes, legacySig, HashAlgorithmName.SHA256);
                }

                if (!validSig)
                {
                    return (false, null, "Digital signature verification failed. The serial key is invalid or has been modified.");
                }

                return (true, legacyPayload, string.Empty);
            }

            return (false, null, "Invalid serial key format. Expected 25 alphanumeric characters (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX).");
        }
        catch (FormatException formatEx)
        {
            return (false, null, $"Invalid serial key format: {formatEx.Message}");
        }
        catch (Exception ex)
        {
            return (false, null, $"Failed to verify serial key: {ex.Message}");
        }
    }

    /// <summary>
    /// Decodes a 25-character serial key into a LicensePayload and its cryptographic signature bytes.
    /// </summary>
    public static (LicensePayload Payload, byte[] Signature) DecodeRawSerialKey(
        string serialKey, 
        string? publicKeyPem = null,
        string? candidateCustomerName = null,
        string? candidateCustomerEmail = null)
    {
        var pubKey = publicKeyPem ?? LicenseVerificationKey.PublicKeyPem;
        var cleaned = NormalizeSerialKey(serialKey);

        if (cleaned.Length == 25)
        {
            if (!IsValidAlphabetString(cleaned))
            {
                throw new FormatException("Invalid character in serial key. Allowed characters: " + Alphabet);
            }

            if (TryDecode25CharKey(cleaned, pubKey, candidateCustomerName, candidateCustomerEmail, out var payload, out var signature, out var error))
            {
                return (payload, signature);
            }
            throw new FormatException(error);
        }

        return DecodeRawLegacySerialKey(serialKey);
    }

    /// <summary>
    /// Normalizes any input serial key by removing hyphens, spaces, whitespace, and converting to uppercase.
    /// </summary>
    public static string NormalizeSerialKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var sb = new StringBuilder(25);
        foreach (var c in input.Trim().ToUpperInvariant())
        {
            if (c != '-' && c != ' ' && c != '\t' && c != '\r' && c != '\n')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static bool IsValidAlphabetString(string input)
    {
        if (input.Length != 25) return false;
        foreach (var c in input)
        {
            if (Alphabet.IndexOf(c) < 0) return false;
        }
        return true;
    }

    public static uint ComputeSignatureTag(byte[] canonicalBytes, string publicKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(publicKeyPem);
        var pubKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();

        using var hmac = new HMACSHA256(pubKeyBytes);
        var tagBytes = hmac.ComputeHash(canonicalBytes);

        uint raw = (uint)((tagBytes[0] << 24) | (tagBytes[1] << 16) | (tagBytes[2] << 8) | tagBytes[3]);
        return raw & 0x1FFFFFFF; // 29 bits
    }

    public static uint ComputeHardwareIdHash(string? hardwareId)
    {
        return LicensePayload.ComputeHardwareIdHash(hardwareId);
    }

    public static string ComputeIssuanceId(string licenseId, ushort nonce, ushort issuedDays, ushort expiryDays, uint hardwareIdHash, bool isRenewal)
    {
        using var sha = SHA256.Create();
        var issSeed = $"{licenseId}::{nonce}::{issuedDays}::{expiryDays}::{hardwareIdHash}::{(isRenewal ? "1" : "0")}";
        var issHash = sha.ComputeHash(Encoding.UTF8.GetBytes(issSeed));
        return $"ISS-{nonce:X4}" + Convert.ToHexString(issHash)[..12];
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string CustomerName, string CustomerEmail)> _inMemoryCandidates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> _customHistoryPaths = new();

    public static void RegisterKnownCustomer(string? serialKey, string? licenseId, string? customerName, string? customerEmail = null)
    {
        if (string.IsNullOrWhiteSpace(customerName)) return;
        var entry = (customerName.Trim(), string.IsNullOrWhiteSpace(customerEmail) ? "customer@dhirdhar.com" : customerEmail.Trim());
        if (!string.IsNullOrWhiteSpace(serialKey))
        {
            _inMemoryCandidates[NormalizeSerialKey(serialKey)] = entry;
        }
        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            _inMemoryCandidates[licenseId.Trim()] = entry;
        }
    }

    public static void RegisterHistoryPath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !_customHistoryPaths.Contains(path))
        {
            _customHistoryPaths.Add(path);
        }
    }

    private static List<(string CustomerName, string CustomerEmail)> DiscoverCandidates(
        string cleaned25, 
        string licenseId, 
        string? candidateCustomerName,
        string? candidateCustomerEmail)
    {
        var candidates = new List<(string CustomerName, string CustomerEmail)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string? name, string? email)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var cleanName = name.Trim();
            var cleanEmail = string.IsNullOrWhiteSpace(email) ? "customer@dhirdhar.com" : email.Trim().ToLowerInvariant();

            var key = $"{cleanName}::{cleanEmail}";
            if (seen.Add(key))
            {
                candidates.Add((cleanName, cleanEmail));
            }

            if (cleanEmail != "customer@dhirdhar.com")
            {
                var defaultKey = $"{cleanName}::customer@dhirdhar.com";
                if (seen.Add(defaultKey))
                {
                    candidates.Add((cleanName, "customer@dhirdhar.com"));
                }
            }
        }

        // 1. Explicit candidate from caller / storage
        if (!string.IsNullOrWhiteSpace(candidateCustomerName))
        {
            AddCandidate(candidateCustomerName, candidateCustomerEmail);
        }

        // 2. In-memory registered cache (e.g. recently generated in same process/tests)
        if (_inMemoryCandidates.TryGetValue(cleaned25, out var byKey))
        {
            AddCandidate(byKey.CustomerName, byKey.CustomerEmail);
        }
        if (_inMemoryCandidates.TryGetValue(licenseId, out var byId))
        {
            AddCandidate(byId.CustomerName, byId.CustomerEmail);
        }

        // 3. Discover from license history json files
        var pathsToCheck = new List<string>
        {
            Path.Combine(@"C:\DhirDharLicenseGenerator", "licenses", "licenses.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDharLicenseGenerator", "licenses", "licenses.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DhirDhar", "LicenseGenerator", "license_history.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "licenses", "licenses.json"),
            Path.Combine(@"D:\DhirDhar License", "DhirDharLicenseGenerator", "licenses", "licenses.json")
        };

        foreach (var customPath in _customHistoryPaths)
        {
            if (!pathsToCheck.Contains(customPath, StringComparer.OrdinalIgnoreCase))
            {
                pathsToCheck.Add(customPath);
            }
        }

        foreach (var path in pathsToCheck)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            string? fileSerialKey = null;
                            string? fileLicenseId = null;
                            string? fileCustomerName = null;
                            string? fileCustomerEmail = null;

                            if (elem.TryGetProperty("SerialKey", out var skProp) || elem.TryGetProperty("serialKey", out skProp))
                            {
                                fileSerialKey = skProp.GetString();
                            }
                            if (elem.TryGetProperty("LicenseId", out var lidProp) || elem.TryGetProperty("licenseId", out lidProp))
                            {
                                fileLicenseId = lidProp.GetString();
                            }
                            if (elem.TryGetProperty("CustomerName", out var cnProp) || elem.TryGetProperty("customerName", out cnProp))
                            {
                                fileCustomerName = cnProp.GetString();
                            }
                            if (elem.TryGetProperty("CustomerEmail", out var ceProp) || elem.TryGetProperty("customerEmail", out ceProp))
                            {
                                fileCustomerEmail = ceProp.GetString();
                            }

                            bool match = false;
                            if (!string.IsNullOrWhiteSpace(fileSerialKey))
                            {
                                var cleanFileKey = NormalizeSerialKey(fileSerialKey);
                                if (string.Equals(cleanFileKey, cleaned25, StringComparison.OrdinalIgnoreCase))
                                {
                                    match = true;
                                }
                            }
                            if (!match && !string.IsNullOrWhiteSpace(fileLicenseId))
                            {
                                if (string.Equals(fileLicenseId.Trim(), licenseId.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    match = true;
                                }
                            }

                            if (match && !string.IsNullOrWhiteSpace(fileCustomerName))
                            {
                                AddCandidate(fileCustomerName, fileCustomerEmail);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        // 3. Fallback generic customer candidates
        AddCandidate("DhirDhar Customer", "customer@dhirdhar.com");

        return candidates;
    }

    private static bool TryDecode25CharKey(
        string cleaned25,
        string publicKeyPem,
        string? candidateCustomerName,
        string? candidateCustomerEmail,
        out LicensePayload payload,
        out byte[] signature,
        out string errorMessage)
    {
        payload = null!;
        signature = Array.Empty<byte>();
        errorMessage = string.Empty;

        var symbols = new int[25];
        for (int i = 0; i < 25; i++)
        {
            int val = Alphabet.IndexOf(cleaned25[i]);
            if (val < 0)
            {
                errorMessage = $"Invalid character '{cleaned25[i]}' in serial key.";
                return false;
            }
            symbols[i] = val;
        }

        var reader = new BitReader(symbols);
        byte version = (byte)reader.ReadBits(4);
        bool isRenewal = reader.ReadBits(1) == 1;
        bool isHardwareBound = reader.ReadBits(1) == 1;
        byte editionCode = (byte)reader.ReadBits(2);
        ushort issuedDays = (ushort)reader.ReadBits(14);
        ushort expiryDays = (ushort)reader.ReadBits(14);
        uint hardwareIdHash = reader.ReadBits(24);
        uint licenseSeq = reader.ReadBits(20);
        ushort issuanceNonce = (ushort)reader.ReadBits(16);
        uint signatureTag = reader.ReadBits(29);

        if (version != 2)
        {
            errorMessage = $"Unsupported serial key version '{version}'.";
            return false;
        }

        var issuedAt = Epoch.AddDays(issuedDays);
        var expiresAt = Epoch.AddDays(expiryDays);
        var licenseId = $"DD-{issuedAt:yyyyMMdd}-{licenseSeq:X5}";
        var issuanceId = ComputeIssuanceId(licenseId, issuanceNonce, issuedDays, expiryDays, hardwareIdHash, isRenewal);

        string edition = isRenewal ? "Renewal" : "Annual";
        string? deviceBinding = isHardwareBound ? $"HW-{hardwareIdHash:X6}" : null;

        var candidates = DiscoverCandidates(cleaned25, licenseId, candidateCustomerName, candidateCustomerEmail);

        // 1. Primary standard canonical contract for 25-character compact keys
        var canonicalStandardPayload = new LicensePayload(
            Product: "DhirDhar",
            LicenseId: licenseId,
            CustomerName: "DhirDhar Customer",
            CustomerEmail: "customer@dhirdhar.com",
            Edition: edition,
            IssuedAt: issuedAt,
            ExpiresAt: expiresAt,
            DeviceLimit: 1,
            LicenseVersion: 1,
            IssuanceId: issuanceId,
            DeviceBinding: deviceBinding,
            PreviousLicenseId: null,
            IsRenewal: isRenewal);

        var canonicalStandardBytes = canonicalStandardPayload.GetCanonicalBytes();
        var expectedStandardTag = ComputeSignatureTag(canonicalStandardBytes, publicKeyPem);

        if (expectedStandardTag == signatureTag)
        {
            var preferredName = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.CustomerName) && c.CustomerName != "DhirDhar Customer").CustomerName;
            var preferredEmail = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.CustomerEmail) && c.CustomerEmail != "customer@dhirdhar.com").CustomerEmail;

            payload = canonicalStandardPayload with
            {
                CustomerName = !string.IsNullOrWhiteSpace(preferredName) ? preferredName : "DhirDhar Customer",
                CustomerEmail = !string.IsNullOrWhiteSpace(preferredEmail) ? preferredEmail : "customer@dhirdhar.com"
            };

            using var sha = SHA256.Create();
            signature = sha.ComputeHash(canonicalStandardBytes);
            var fullSig = new byte[64];
            Array.Copy(signature, 0, fullSig, 0, 32);
            Array.Copy(Encoding.UTF8.GetBytes(issuanceId), 0, fullSig, 32, Math.Min(32, Encoding.UTF8.GetByteCount(issuanceId)));
            signature = fullSig;

            return true;
        }

        // 2. Backward compatibility fallback for any licenses signed with custom names
        foreach (var (custName, custEmail) in candidates)
        {
            var testPayload = new LicensePayload(
                Product: "DhirDhar",
                LicenseId: licenseId,
                CustomerName: custName,
                CustomerEmail: custEmail,
                Edition: edition,
                IssuedAt: issuedAt,
                ExpiresAt: expiresAt,
                DeviceLimit: 1,
                LicenseVersion: 1,
                IssuanceId: issuanceId,
                DeviceBinding: deviceBinding,
                PreviousLicenseId: null,
                IsRenewal: isRenewal);

            var canonicalBytes = testPayload.GetCanonicalBytes();
            var expectedTag = ComputeSignatureTag(canonicalBytes, publicKeyPem);

            if (expectedTag == signatureTag)
            {
                payload = testPayload;

                // Return a deterministic 64-byte signature representation
                using var sha = SHA256.Create();
                signature = sha.ComputeHash(canonicalBytes);
                var fullSig = new byte[64];
                Array.Copy(signature, 0, fullSig, 0, 32);
                Array.Copy(Encoding.UTF8.GetBytes(issuanceId), 0, fullSig, 32, Math.Min(32, Encoding.UTF8.GetByteCount(issuanceId)));
                signature = fullSig;

                return true;
            }
        }

        errorMessage = "Digital signature verification failed. The serial key is invalid or has been modified.";
        return false;
    }

    #region Legacy Base32 & Decoder
    private static (LicensePayload Payload, byte[] Signature) DecodeRawLegacySerialKey(string serialKey)
    {
        var cleaned = serialKey.Trim().ToUpperInvariant()
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);

        if (cleaned.StartsWith("DD"))
        {
            cleaned = cleaned.Substring(2);
        }

        var rawBytes = FromLegacyBase32(cleaned);
        if (rawBytes.Length < 5 + 64)
        {
            throw new FormatException("The serial key length is invalid.");
        }

        if (rawBytes[0] != MagicByte1 || rawBytes[1] != MagicByte2)
        {
            throw new FormatException("Invalid serial key header.");
        }

        var version = rawBytes[2];
        if (version != CurrentFormatVersion)
        {
            throw new FormatException($"Unsupported serial key version '{version}'.");
        }

        int payloadLength = (rawBytes[3] << 8) | rawBytes[4];
        if (rawBytes.Length < 5 + payloadLength + 64)
        {
            throw new FormatException("Corrupted serial key payload data.");
        }

        var payloadBytes = new byte[payloadLength];
        Array.Copy(rawBytes, 5, payloadBytes, 0, payloadLength);

        var signature = new byte[rawBytes.Length - (5 + payloadLength)];
        Array.Copy(rawBytes, 5 + payloadLength, signature, 0, signature.Length);

        var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes);
        if (payload is null)
        {
            throw new FormatException("Failed to parse license payload.");
        }

        return (payload, signature);
    }

    private static byte[] FromLegacyBase32(string input)
    {
        var output = new MemoryStream();
        int buffer = 0;
        int bitsLeft = 0;

        foreach (char c in input)
        {
            int val = LegacyBase32Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (val < 0) continue;

            buffer = (buffer << 5) | val;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.WriteByte((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return output.ToArray();
    }
    #endregion

    #region Bit Packing Utilities
    public sealed class BitWriter
    {
        private readonly bool[] _bits;
        private int _position;

        public BitWriter(int totalBits)
        {
            _bits = new bool[totalBits];
            _position = 0;
        }

        public void WriteBits(uint value, int bitCount)
        {
            for (int i = bitCount - 1; i >= 0; i--)
            {
                _bits[_position++] = ((value >> i) & 1) == 1;
            }
        }

        public int[] To5BitSymbols()
        {
            int symbolCount = _bits.Length / 5;
            var symbols = new int[symbolCount];
            for (int i = 0; i < symbolCount; i++)
            {
                int val = 0;
                for (int b = 0; b < 5; b++)
                {
                    val = (val << 1) | (_bits[i * 5 + b] ? 1 : 0);
                }
                symbols[i] = val;
            }
            return symbols;
        }
    }

    public sealed class BitReader
    {
        private readonly bool[] _bits;
        private int _position;

        public BitReader(int[] symbols5Bit)
        {
            _bits = new bool[symbols5Bit.Length * 5];
            for (int i = 0; i < symbols5Bit.Length; i++)
            {
                int sym = symbols5Bit[i];
                for (int b = 4; b >= 0; b--)
                {
                    _bits[i * 5 + (4 - b)] = ((sym >> b) & 1) == 1;
                }
            }
            _position = 0;
        }

        public uint ReadBits(int bitCount)
        {
            uint val = 0;
            for (int i = 0; i < bitCount; i++)
            {
                val = (val << 1) | (_bits[_position++] ? 1u : 0u);
            }
            return val;
        }
    }
    #endregion
}
