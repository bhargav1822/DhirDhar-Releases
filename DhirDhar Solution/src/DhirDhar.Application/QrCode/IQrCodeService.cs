using System;

namespace DhirDhar.Application.QrCode;

public interface IQrCodeService
{
    /// <summary>
    /// Formats the official DhirDhar QR payload for a given borrower/account number.
    /// Format: DHIRDHAR|ACCOUNT|{BorrowerNumber}
    /// </summary>
    string FormatPayload(string borrowerNumber);

    /// <summary>
    /// Validates and parses the scanned QR payload into the target account's BorrowerNumber.
    /// Rejects invalid formats and returns false.
    /// </summary>
    bool TryParsePayload(string? rawQrContent, out string borrowerNumber);

    /// <summary>
    /// Generates standard PNG image bytes for the account's QR code.
    /// </summary>
    byte[] GeneratePngBytes(string borrowerNumber, int pixelsPerModule = 10);
}
