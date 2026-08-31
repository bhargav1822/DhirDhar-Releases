using System;
using DhirDhar.Application.QrCode;
using QRCoder;

namespace DhirDhar.Infrastructure.QrCode;

public sealed class QrCodeService : IQrCodeService
{
    private const string PayloadPrefix = "DHIRDHAR|ACCOUNT|";

    public string FormatPayload(string borrowerNumber)
    {
        if (string.IsNullOrWhiteSpace(borrowerNumber))
        {
            throw new ArgumentException("Borrower number cannot be empty.", nameof(borrowerNumber));
        }

        return $"{PayloadPrefix}{borrowerNumber.Trim()}";
    }

    public bool TryParsePayload(string? rawQrContent, out string borrowerNumber)
    {
        borrowerNumber = string.Empty;

        if (string.IsNullOrWhiteSpace(rawQrContent))
        {
            return false;
        }

        var trimmed = rawQrContent.Trim();

        // 1. Official DhirDhar QR Format: DHIRDHAR|ACCOUNT|{BorrowerNumber}
        if (trimmed.StartsWith(PayloadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var extracted = trimmed[PayloadPrefix.Length..].Trim().TrimStart('#').Trim();
            if (!string.IsNullOrWhiteSpace(extracted))
            {
                borrowerNumber = extracted;
                return true;
            }
            return false;
        }

        // 2. Also support if barcode scanner or user pasted exact borrower number directly (e.g. DJ102, #DJ102, B-12345)
        // Ensure it doesn't contain forbidden URLs, non-DhirDhar payloads, or malicious scripts
        if (!trimmed.Contains('|') && !trimmed.Contains("://") && !trimmed.Contains('\n') && !trimmed.Contains('\r') && trimmed.Length <= 50)
        {
            var cleanNumber = trimmed.TrimStart('#').Trim();
            if (!string.IsNullOrWhiteSpace(cleanNumber))
            {
                borrowerNumber = cleanNumber;
                return true;
            }
        }

        return false;
    }

    public byte[] GeneratePngBytes(string borrowerNumber, int pixelsPerModule = 10)
    {
        var payload = FormatPayload(borrowerNumber);

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrCodeData);

        return qrCode.GetGraphic(pixelsPerModule);
    }
}
