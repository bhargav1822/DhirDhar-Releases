using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DhirDhar.Application.Licensing.Models;

/// <summary>
/// Structured cryptographic license payload contract shared between
/// the DhirDhar License Generator and the MAIN DhirDhar application.
/// </summary>
[JsonConverter(typeof(LicensePayloadJsonConverter))]
public sealed record LicensePayload
{
    public string Product { get; init; } = "DhirDhar";
    public string LicenseId { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string LicenseType { get; init; } = "Annual";
    public DateTime IssuedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public int DeviceLimit { get; init; } = 1;
    public string? DeviceBinding { get; init; }
    public int LicenseVersion { get; init; } = 1;
    public string IssuanceId { get; init; } = string.Empty;
    public string? PreviousLicenseId { get; init; }
    public bool Renewal { get; init; }

    // Compatibility accessors for code expecting Edition / IsRenewal
    [JsonIgnore]
    public string Edition => LicenseType;

    [JsonIgnore]
    public bool IsRenewal => Renewal;

    public LicensePayload() { }

    public LicensePayload(
        string Product,
        string LicenseId,
        string CustomerName,
        string CustomerEmail,
        string Edition,
        DateTime IssuedAt,
        DateTime ExpiresAt,
        int DeviceLimit,
        int LicenseVersion,
        string IssuanceId,
        string? DeviceBinding = null,
        string? PreviousLicenseId = null,
        bool IsRenewal = false)
    {
        this.Product = Product;
        this.LicenseId = LicenseId;
        this.CustomerName = CustomerName;
        this.CustomerEmail = CustomerEmail;
        this.LicenseType = Edition;
        this.IssuedAt = IssuedAt;
        this.ExpiresAt = ExpiresAt;
        this.DeviceLimit = DeviceLimit;
        this.LicenseVersion = LicenseVersion;
        this.IssuanceId = IssuanceId;
        this.DeviceBinding = DeviceBinding;
        this.PreviousLicenseId = PreviousLicenseId;
        this.Renewal = IsRenewal;
    }

    /// <summary>
    /// Creates a canonical UTF-8 byte array used for cryptographic signing and signature verification.
    /// This ensures cross-platform and cross-environment determinism.
    /// </summary>
    public byte[] GetCanonicalBytes()
    {
        var canonicalString = string.Join("|",
            (Product ?? string.Empty).Trim(),
            (LicenseId ?? string.Empty).Trim(),
            (CustomerName ?? string.Empty).Trim(),
            (CustomerEmail ?? string.Empty).Trim().ToLowerInvariant(),
            (LicenseType ?? string.Empty).Trim(),
            IssuedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ExpiresAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            DeviceLimit.ToString(CultureInfo.InvariantCulture),
            LicenseVersion.ToString(CultureInfo.InvariantCulture),
            (IssuanceId ?? string.Empty).Trim(),
            (DeviceBinding ?? string.Empty).Trim(),
            (PreviousLicenseId ?? string.Empty).Trim(),
            Renewal ? "1" : "0");

        return Encoding.UTF8.GetBytes(canonicalString);
    }

    /// <summary>
    /// Fallback canonical format for legacy v1 licenses issued prior to IssuanceId / Renewal fields.
    /// </summary>
    public byte[] GetLegacyCanonicalBytes()
    {
        var canonicalString = string.Join("|",
            (Product ?? string.Empty).Trim(),
            (LicenseId ?? string.Empty).Trim(),
            (CustomerName ?? string.Empty).Trim(),
            (CustomerEmail ?? string.Empty).Trim().ToLowerInvariant(),
            (LicenseType ?? string.Empty).Trim(),
            IssuedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ExpiresAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            DeviceLimit.ToString(CultureInfo.InvariantCulture),
            LicenseVersion.ToString(CultureInfo.InvariantCulture));

        return Encoding.UTF8.GetBytes(canonicalString);
    }

    /// <summary>
    /// Computes a compact 24-bit hash of a device hardware ID for serial key packing.
    /// Returns 0 for unbound licenses.
    /// </summary>
    public static uint ComputeHardwareIdHash(string? hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            return 0;
        }

        var cleaned = hardwareId.Trim().ToUpperInvariant();
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(cleaned));
        return (uint)((hash[0] << 16) | (hash[1] << 8) | hash[2]) & 0x00FFFFFF;
    }
}

/// <summary>
/// Custom JsonConverter to guarantee consistent canonical JSON property serialization
/// and resilient backward-compatible deserialization for all aliases and casing variations.
/// </summary>
public sealed class LicensePayloadJsonConverter : JsonConverter<LicensePayload>
{
    public override LicensePayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token.");
        }

        string product = "DhirDhar";
        string licenseId = string.Empty;
        string customerName = string.Empty;
        string customerEmail = string.Empty;
        string licenseType = "Annual";
        DateTime issuedAt = DateTime.UtcNow;
        DateTime expiresAt = DateTime.UtcNow.AddDays(365);
        int deviceLimit = 1;
        string? deviceBinding = null;
        int licenseVersion = 1;
        string issuanceId = string.Empty;
        string? previousLicenseId = null;
        bool renewal = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propName = reader.GetString();
                reader.Read();

                if (string.IsNullOrEmpty(propName)) continue;

                switch (propName.Trim().ToLowerInvariant())
                {
                    case "product":
                    case "p":
                        product = reader.GetString() ?? product;
                        break;

                    case "licenseid":
                    case "lid":
                        licenseId = reader.GetString() ?? licenseId;
                        break;

                    case "customername":
                    case "cn":
                        customerName = reader.GetString() ?? customerName;
                        break;

                    case "customeremail":
                    case "ce":
                        customerEmail = reader.GetString() ?? customerEmail;
                        break;

                    case "licensetype":
                    case "edition":
                    case "ed":
                        licenseType = reader.GetString() ?? licenseType;
                        break;

                    case "issuedat":
                    case "iat":
                        if (reader.TokenType == JsonTokenType.String &&
                            DateTime.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedIat))
                        {
                            issuedAt = parsedIat;
                        }
                        break;

                    case "expiresat":
                    case "exp":
                        if (reader.TokenType == JsonTokenType.String &&
                            DateTime.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedExp))
                        {
                            expiresAt = parsedExp;
                        }
                        break;

                    case "devicelimit":
                    case "dl":
                        if (reader.TokenType == JsonTokenType.Number)
                        {
                            deviceLimit = reader.GetInt32();
                        }
                        break;

                    case "devicebinding":
                    case "db":
                        deviceBinding = reader.GetString();
                        break;

                    case "licenseversion":
                    case "v":
                        if (reader.TokenType == JsonTokenType.Number)
                        {
                            licenseVersion = reader.GetInt32();
                        }
                        break;

                    case "issuanceid":
                    case "issuance_id":
                    case "issueid":
                    case "issuedid":
                    case "iid":
                        issuanceId = reader.GetString() ?? issuanceId;
                        break;

                    case "previouslicenseid":
                    case "previous_license_id":
                    case "plid":
                        previousLicenseId = reader.GetString();
                        break;

                    case "renewal":
                    case "isrenewal":
                    case "is_renewal":
                    case "ren":
                        if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                        {
                            renewal = reader.GetBoolean();
                        }
                        break;
                }
            }
        }

        return new LicensePayload(
            Product: product,
            LicenseId: licenseId,
            CustomerName: customerName,
            CustomerEmail: customerEmail,
            Edition: licenseType,
            IssuedAt: issuedAt,
            ExpiresAt: expiresAt,
            DeviceLimit: deviceLimit,
            LicenseVersion: licenseVersion,
            IssuanceId: issuanceId,
            DeviceBinding: deviceBinding,
            PreviousLicenseId: previousLicenseId,
            IsRenewal: renewal);
    }

    public override void Write(Utf8JsonWriter writer, LicensePayload value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Product", value.Product);
        writer.WriteString("LicenseId", value.LicenseId);
        writer.WriteString("CustomerName", value.CustomerName);
        writer.WriteString("CustomerEmail", value.CustomerEmail);
        writer.WriteString("LicenseType", value.LicenseType);
        writer.WriteString("IssuedAt", value.IssuedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        writer.WriteString("ExpiresAt", value.ExpiresAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        writer.WriteNumber("DeviceLimit", value.DeviceLimit);

        if (value.DeviceBinding != null)
        {
            writer.WriteString("DeviceBinding", value.DeviceBinding);
        }
        else
        {
            writer.WriteNull("DeviceBinding");
        }

        writer.WriteNumber("LicenseVersion", value.LicenseVersion);
        writer.WriteString("IssuanceId", value.IssuanceId);

        if (value.PreviousLicenseId != null)
        {
            writer.WriteString("PreviousLicenseId", value.PreviousLicenseId);
        }
        else
        {
            writer.WriteNull("PreviousLicenseId");
        }

        writer.WriteBoolean("Renewal", value.Renewal);
        writer.WriteEndObject();
    }
}
