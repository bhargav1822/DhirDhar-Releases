using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Printing;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Printing;

[SupportedOSPlatform("windows6.1")]
public sealed class WindowsPrinterService : IPrintService
{
    private readonly IDatabasePathService _pathService;
    private readonly ILogger<WindowsPrinterService> _logger;

    public WindowsPrinterService(IDatabasePathService pathService, ILogger<WindowsPrinterService> logger)
    {
        _pathService = pathService;
        _logger = logger;
    }

    public Task<string> GenerateReceiptPdfAsync(ReceiptData receipt, CancellationToken cancellationToken = default)
    {
        if (receipt == null) throw new ArgumentNullException(nameof(receipt));

        var exportDir = Path.Combine(_pathService.ApplicationDataDirectory, "Receipts");
        var pdfPath = PosReceiptBuilder.BuildReceiptPdf(receipt, exportDir);
        _logger.LogInformation("Receipt PDF generated at '{PdfPath}' for type {Type}", pdfPath, receipt.Type);
        return Task.FromResult(pdfPath);
    }

    public async Task<bool> PrintReceiptAsync(ReceiptData receipt, string? printerName = null, CancellationToken cancellationToken = default)
    {
        if (receipt == null) throw new ArgumentNullException(nameof(receipt));

        var pdfPath = await GenerateReceiptPdfAsync(receipt, cancellationToken);
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("Receipt PDF could not be created for printing.", pdfPath);
        }

        var targetPrinter = string.IsNullOrWhiteSpace(printerName)
            ? GetDefaultPrinterName()
            : printerName.Trim();

        if (string.IsNullOrWhiteSpace(targetPrinter))
        {
            _logger.LogWarning("No default printer available. PDF generated at '{PdfPath}'.", pdfPath);
            return true;
        }

        // Validate printer existence
        var installed = GetInstalledPrinters();
        var match = installed.FirstOrDefault(p => string.Equals(p, targetPrinter, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            _logger.LogWarning("Printer '{PrinterName}' not found in installed printers list.", targetPrinter);
            // Still proceed to attempt system dispatch, or throw if strictly unavailable
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pdfPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (!string.IsNullOrEmpty(match))
            {
                psi.Verb = "printto";
                psi.Arguments = $"\"{match}\"";
            }
            else
            {
                psi.Verb = "print";
            }

            using var proc = Process.Start(psi);
            _logger.LogInformation("Sent receipt '{PdfPath}' to printer '{PrinterName}'", pdfPath, targetPrinter);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to print receipt to '{PrinterName}'", targetPrinter);
            throw new InvalidOperationException($"Printing to '{targetPrinter}' failed: {ex.Message}", ex);
        }
    }

    public IReadOnlyList<string> GetInstalledPrinters()
    {
        var list = new List<string>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    if (!string.IsNullOrWhiteSpace(printer) && !list.Contains(printer, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(printer);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to query installed Windows printers via PrinterSettings.");
        }

        if (list.Count == 0)
        {
            list.Add("Microsoft Print to PDF");
        }

        return list;
    }

    public string? GetDefaultPrinterName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var settings = new PrinterSettings();
                if (!string.IsNullOrWhiteSpace(settings.PrinterName) && settings.IsValid)
                {
                    return settings.PrinterName;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to retrieve default printer.");
        }

        var installed = GetInstalledPrinters();
        return installed.FirstOrDefault();
    }
}
