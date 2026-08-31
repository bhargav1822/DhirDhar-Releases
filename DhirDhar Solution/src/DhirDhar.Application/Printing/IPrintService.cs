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
}
