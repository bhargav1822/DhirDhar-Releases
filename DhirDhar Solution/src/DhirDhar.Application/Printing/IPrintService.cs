using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Printing;

public interface IPrintService
{
    Task<string> GenerateReceiptPdfAsync(ReceiptData receipt, CancellationToken cancellationToken = default);
    Task<bool> PrintReceiptAsync(ReceiptData receipt, string? printerName = null, CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetInstalledPrinters();
    string? GetDefaultPrinterName();
    IReadOnlyList<PrinterPaperSizeInfo> GetSupportedPaperSizes(string? printerName = null);
    bool IsThermalPrinter(string? printerName);
    Task<bool> PrintTestReceiptAsync(string? printerName, string? paperSizeName, bool autoCut, string? languageCode = null, CancellationToken cancellationToken = default);
}
