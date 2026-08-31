using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Printing;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Printing;

[SupportedOSPlatform("windows6.1")]
public sealed class WindowsPrinterService : IPrintService
{
    private readonly IDatabasePathService _pathService;
    private readonly ILocalizationService? _localizationService;
    private readonly ILogger<WindowsPrinterService> _logger;

    public WindowsPrinterService(
        IDatabasePathService pathService,
        ILogger<WindowsPrinterService> logger,
        ILocalizationService? localizationService = null)
    {
        _pathService = pathService;
        _logger = logger;
        _localizationService = localizationService;
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

        var targetPrinter = string.IsNullOrWhiteSpace(printerName)
            ? GetDefaultPrinterName()
            : printerName.Trim();

        if (string.IsNullOrWhiteSpace(targetPrinter))
        {
            var noPrinterMsg = _localizationService?.GetString("NoPrinterSelected") ?? "No printer selected.";
            throw new InvalidOperationException(noPrinterMsg);
        }

        var installed = GetInstalledPrinters();
        if (!installed.Any(p => string.Equals(p, targetPrinter, StringComparison.OrdinalIgnoreCase)))
        {
            var unavailableMsg = _localizationService?.GetString("PrinterUnavailable") ?? $"Printer '{targetPrinter}' is unavailable or not found.";
            throw new InvalidOperationException(unavailableMsg);
        }

        var pdfPath = await GenerateReceiptPdfAsync(receipt, cancellationToken);
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("Receipt PDF could not be created for printing.", pdfPath);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pdfPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "printto",
                Arguments = $"\"{targetPrinter}\""
            };

            using var proc = Process.Start(psi);
            _logger.LogInformation("Sent receipt '{PdfPath}' to printer '{PrinterName}'", pdfPath, targetPrinter);

            if (receipt.AutoCut && IsThermalPrinter(targetPrinter))
            {
                TrySendCutCommand(targetPrinter);
            }

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

    public IReadOnlyList<PrinterPaperSizeInfo> GetSupportedPaperSizes(string? printerName = null)
    {
        var result = new List<PrinterPaperSizeInfo>();
        var targetPrinter = string.IsNullOrWhiteSpace(printerName) ? GetDefaultPrinterName() : printerName.Trim();

        if (string.IsNullOrWhiteSpace(targetPrinter))
        {
            return GetDefaultFallbackPaperSizes();
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var settings = new PrinterSettings { PrinterName = targetPrinter };
                if (settings.IsValid)
                {
                    var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (System.Drawing.Printing.PaperSize ps in settings.PaperSizes)
                    {
                        if (string.IsNullOrWhiteSpace(ps.PaperName) || seenNames.Contains(ps.PaperName))
                        {
                            continue;
                        }

                        seenNames.Add(ps.PaperName);

                        // Hundredths of an inch to mm (1 in = 25.4 mm = 100 hundredths)
                        double widthMm = Math.Round(ps.Width * 0.254, 1);
                        double heightMm = Math.Round(ps.Height * 0.254, 1);

                        bool isContinuous = PaperSizeHelper.IsThermalPosSize(ps.PaperName) ||
                                            (widthMm is >= 40.0 and <= 120.0 && heightMm >= 200.0);

                        string displayLabel = FormatPaperSizeLabel(ps.PaperName, widthMm, heightMm);
                        result.Add(new PrinterPaperSizeInfo(ps.PaperName, displayLabel, (int)ps.Kind, widthMm, heightMm, isContinuous));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query paper sizes for printer '{PrinterName}'", targetPrinter);
        }

        if (result.Count == 0)
        {
            return GetDefaultFallbackPaperSizes();
        }

        return result;
    }

    private static string FormatPaperSizeLabel(string name, double widthMm, double heightMm)
    {
        if (name.Contains("mm", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(" x ", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(" × ", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("in", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        if (widthMm > 0 && heightMm > 0)
        {
            return $"{name} ({widthMm:0.#} × {heightMm:0.#} mm)";
        }

        return name;
    }

    private IReadOnlyList<PrinterPaperSizeInfo> GetDefaultFallbackPaperSizes()
    {
        var a4Label = _localizationService?.GetString("PaperSizeA4") ?? "A4 (210 × 297 mm)";
        var a5Label = _localizationService?.GetString("PaperSizeA5") ?? "A5 (148 × 210 mm)";
        var letterLabel = _localizationService?.GetString("PaperSizeLetter") ?? "Letter (8.5 × 11 in)";
        var pos58Label = _localizationService?.GetString("PaperSizePOS58") ?? "POS 58 mm (Thermal)";
        var pos80Label = _localizationService?.GetString("PaperSizePOS80") ?? "POS 80 mm (Thermal)";

        return new List<PrinterPaperSizeInfo>
        {
            new("A4", a4Label, (int)PaperKind.A4, 210.0, 297.0, false),
            new("A5", a5Label, (int)PaperKind.A5, 148.0, 210.0, false),
            new("Letter", letterLabel, (int)PaperKind.Letter, 215.9, 279.4, false),
            new("POS80", pos80Label, (int)PaperKind.Custom, 80.0, 297.0, true),
            new("POS58", pos58Label, (int)PaperKind.Custom, 58.0, 297.0, true)
        };
    }

    public bool IsThermalPrinter(string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return false;

        var upper = printerName.ToUpperInvariant();
        var keywords = new[]
        {
            "POS", "THERMAL", "RECEIPT", "TM-", "TSP", "RP-", "XP-", "XP58", "XP80", "58MM", "80MM",
            "CITIZEN", "STAR MICRONICS", "BIXOLON", "SEWOO", "RONGTA", "HOIN", "MUNBYN", "ZJ-", "GOOJPRT"
        };

        if (keywords.Any(upper.Contains))
        {
            return true;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var settings = new PrinterSettings { PrinterName = printerName };
                if (settings.IsValid)
                {
                    // Check if driver name contains thermal indicators
                    var paperSizes = settings.PaperSizes;
                    int rollCount = 0;
                    foreach (System.Drawing.Printing.PaperSize ps in paperSizes)
                    {
                        if (PaperSizeHelper.IsThermalPosSize(ps.PaperName))
                        {
                            rollCount++;
                        }
                    }

                    if (rollCount > 0 && rollCount >= paperSizes.Count / 2)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignore inspection errors
        }

        return false;
    }

    public async Task<bool> PrintTestReceiptAsync(
        string? printerName,
        string? paperSizeName,
        bool autoCut,
        string? languageCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(printerName) || string.Equals(printerName, "NONE", StringComparison.OrdinalIgnoreCase))
        {
            var noPrinterMsg = _localizationService?.GetString("NoPrinterSelected") ?? "No printer selected.";
            throw new InvalidOperationException(noPrinterMsg);
        }

        var targetPrinter = printerName.Trim();
        var installed = GetInstalledPrinters();
        if (!installed.Any(p => string.Equals(p, targetPrinter, StringComparison.OrdinalIgnoreCase)))
        {
            var unavailableMsg = _localizationService?.GetString("PrinterUnavailable") ?? $"Printer '{targetPrinter}' is unavailable or offline.";
            throw new InvalidOperationException(unavailableMsg);
        }

        var lang = string.IsNullOrWhiteSpace(languageCode)
            ? (_localizationService?.CurrentLanguage ?? "en-IN")
            : languageCode;

        var selectedPaper = string.IsNullOrWhiteSpace(paperSizeName) ? "A4" : paperSizeName;

        // Build localized strings for receipt
        var headerTitle = _localizationService?.GetString("PrintTestReceiptTitle", lang) ?? "PRINT TEST RECEIPT";
        var statusOk = _localizationService?.GetString("StatusPrintingOk", lang) ?? "Printing OK";
        var printerLabel = _localizationService?.GetString("SelectedPrinter", lang) ?? "Printer";
        var paperLabel = _localizationService?.GetString("PaperSize", lang) ?? "Paper";
        var dateLabel = _localizationService?.GetString("Date", lang) ?? "Date";
        var statusLabel = _localizationService?.GetString("Status", lang) ?? "Status";
        var thankYouNote = _localizationService?.GetString("ThankYouForYourBusiness", lang) ?? "Thank You For Using DhirDhar";

        var isThermal = IsThermalPrinter(targetPrinter) || PaperSizeHelper.IsThermalPosSize(selectedPaper);

        var sample = new ReceiptData
        {
            Type = ReceiptType.BorrowerReceipt,
            BusinessName = "DhirDhar Solution",
            Title = headerTitle,
            Subtitle = "--------------------------------------------------",
            BorrowerName = $"{printerLabel}: {targetPrinter}",
            BorrowerNumber = $"{paperLabel}: {selectedPaper}",
            Contact = $"{dateLabel}: {DateTime.Now:dd-MM-yyyy HH:mm:ss}",
            Village = $"{statusLabel}: {statusOk}",
            PaperSize = selectedPaper,
            AutoCut = autoCut && isThermal,
            LanguageCode = lang,
            FooterNote = thankYouNote,
            CreatedAt = DateTime.Now
        };

        // 1. Generate PDF archive
        var exportDir = Path.Combine(_pathService.ApplicationDataDirectory, "Receipts");
        var pdfPath = PosReceiptBuilder.BuildReceiptPdf(sample, exportDir);

        // 2. Perform native Windows PrintDocument print
        await Task.Run(() =>
        {
            using var printDoc = new PrintDocument();
            printDoc.PrinterSettings.PrinterName = targetPrinter;

            if (!printDoc.PrinterSettings.IsValid)
            {
                var msg = _localizationService?.GetString("PrinterUnavailable", lang) ?? $"Printer '{targetPrinter}' is invalid or offline.";
                throw new InvalidOperationException(msg);
            }

            // Find matching paper size if present
            foreach (System.Drawing.Printing.PaperSize ps in printDoc.PrinterSettings.PaperSizes)
            {
                if (string.Equals(ps.PaperName, selectedPaper, StringComparison.OrdinalIgnoreCase))
                {
                    printDoc.DefaultPageSettings.PaperSize = ps;
                    break;
                }
            }

            printDoc.PrintPage += (sender, e) =>
            {
                if (e.Graphics == null) return;

                using var titleFont = new Font("Segoe UI", 12, FontStyle.Bold);
                using var boldFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                using var regularFont = new Font("Segoe UI", 9, FontStyle.Regular);
                using var monoFont = new Font("Consolas", 8.5f, FontStyle.Regular);

                float leftMargin = isThermal ? 10f : 40f;
                float topMargin = isThermal ? 15f : 40f;
                float currentY = topMargin;
                float pageWidth = e.PageBounds.Width - (leftMargin * 2);

                using var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
                using var sfLeft = new StringFormat { Alignment = StringAlignment.Near };

                // Business Name
                e.Graphics.DrawString("DhirDhar Solution", titleFont, Brushes.Black, new RectangleF(leftMargin, currentY, pageWidth, 22), sfCenter);
                currentY += 24;

                // Separator
                e.Graphics.DrawString(new string('-', isThermal ? 32 : 55), monoFont, Brushes.Gray, new RectangleF(leftMargin, currentY, pageWidth, 15), sfCenter);
                currentY += 16;

                // Title
                e.Graphics.DrawString(headerTitle, boldFont, Brushes.Black, new RectangleF(leftMargin, currentY, pageWidth, 18), sfCenter);
                currentY += 20;

                // Separator
                e.Graphics.DrawString(new string('-', isThermal ? 32 : 55), monoFont, Brushes.Gray, new RectangleF(leftMargin, currentY, pageWidth, 15), sfCenter);
                currentY += 18;

                // Key-Values
                e.Graphics.DrawString($"{printerLabel}: {targetPrinter}", regularFont, Brushes.Black, leftMargin, currentY);
                currentY += 16;

                e.Graphics.DrawString($"{paperLabel}: {selectedPaper}", regularFont, Brushes.Black, leftMargin, currentY);
                currentY += 16;

                e.Graphics.DrawString($"{dateLabel}: {DateTime.Now:dd-MM-yyyy HH:mm:ss}", regularFont, Brushes.Black, leftMargin, currentY);
                currentY += 16;

                e.Graphics.DrawString($"{statusLabel}: {statusOk}", boldFont, Brushes.DarkGreen, leftMargin, currentY);
                currentY += 22;

                // Separator
                e.Graphics.DrawString(new string('-', isThermal ? 32 : 55), monoFont, Brushes.Gray, new RectangleF(leftMargin, currentY, pageWidth, 15), sfCenter);
                currentY += 18;

                // Footer
                e.Graphics.DrawString(thankYouNote, regularFont, Brushes.DarkSlateGray, new RectangleF(leftMargin, currentY, pageWidth, 20), sfCenter);

                e.HasMorePages = false;
            };

            printDoc.Print();
            _logger.LogInformation("Successfully printed test receipt to '{PrinterName}' with paper '{PaperSize}'", targetPrinter, selectedPaper);
        }, cancellationToken);

        if (autoCut && isThermal)
        {
            TrySendCutCommand(targetPrinter);
        }

        return true;
    }

    private void TrySendCutCommand(string printerName)
    {
        try
        {
            // Standard ESC/POS Paper Cut Command: GS V 66 0 (Cut with feed)
            byte[] cutCommand = new byte[] { 0x1D, 0x56, 0x42, 0x00 };
            RawPrinterHelper.SendBytesToPrinter(printerName, cutCommand);
            _logger.LogInformation("Sent ESC/POS cut command to '{PrinterName}'", printerName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send raw cut command to '{PrinterName}'", printerName);
        }
    }
}

/// <summary>
/// Native Win32 Spooler raw byte sender for ESC/POS commands (such as paper cut).
/// </summary>
internal static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pDocName;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static bool SendBytesToPrinter(string szPrinterName, byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return false;

        IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
        Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

        try
        {
            if (OpenPrinter(szPrinterName.Normalize(), out IntPtr hPrinter, IntPtr.Zero))
            {
                var di = new DOCINFOA
                {
                    pDocName = "DhirDhar ESC/POS Command",
                    pDataType = "RAW"
                };

                if (StartDocPrinter(hPrinter, 1, di))
                {
                    if (StartPagePrinter(hPrinter))
                    {
                        WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out _);
                        EndPagePrinter(hPrinter);
                    }
                    EndDocPrinter(hPrinter);
                }
                ClosePrinter(hPrinter);
                return true;
            }
            return false;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pUnmanagedBytes);
        }
    }
}
