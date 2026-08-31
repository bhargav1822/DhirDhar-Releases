using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Reports;
using DhirDhar.Application.Reports.Models;
using DhirDhar.Infrastructure.Localization;
using Microsoft.Extensions.Logging;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace DhirDhar.Infrastructure.Reports;

public sealed class PdfExportService : IPdfExportService
{
    private readonly IDatabasePathService _pathService;
    private readonly ILocalizationService? _localizationService;
    private readonly ITranslationService? _translationService;
    private readonly ILogger<PdfExportService> _logger;

    static PdfExportService()
    {
        try
        {
            if (GlobalFontSettings.FontResolver is not IndicFontResolver)
            {
                GlobalFontSettings.FontResolver = new IndicFontResolver();
            }
        }
        catch
        {
            // Ignore if font resolver was already initialized elsewhere
        }
    }

    public PdfExportService(
        IDatabasePathService pathService,
        ILogger<PdfExportService> logger,
        ILocalizationService? localizationService = null,
        ITranslationService? translationService = null)
    {
        _pathService = pathService;
        _logger = logger;
        _localizationService = localizationService;
        _translationService = translationService;
    }

    private string GetCurrentLanguage() => _localizationService?.CurrentLanguage ?? LocalizationService.ResolveInitialLanguage();

    private string L(string key, string? defaultEnglish = null)
    {
        if (_localizationService != null)
        {
            var val = _localizationService.GetString(key);
            if (!string.IsNullOrWhiteSpace(val) && !val.Equals(key, StringComparison.Ordinal))
            {
                return val;
            }
        }
        return defaultEnglish ?? key;
    }

    private string LocalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lang = GetCurrentLanguage();
        if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return text;
        return _translationService?.Translate(text, lang) ?? ScriptTranslator.Translate(text, lang);
    }

    private string LocalizeDigits(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;
        var lang = GetCurrentLanguage();
        if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.NormalizeDigitsToAscii(input);
        }
        return _localizationService?.LocalizeDigits(input) ?? ScriptTranslator.ConvertDigitsToIndic(input, lang);
    }

    private string FormatCurrency(decimal amount)
    {
        var formatted = $"₹ {amount:N2}";
        return LocalizeDigits(formatted);
    }

    private string FormatDate(DateTime date, bool includeTime = false)
    {
        var formatted = includeTime ? date.ToString("dd-MM-yyyy hh:mm tt") : date.ToString("dd-MM-yyyy");
        return LocalizeDigits(formatted);
    }

    private string FormatPercent(decimal percent)
    {
        var formatted = $"{percent:N2}%";
        return LocalizeDigits(formatted);
    }

    private string GetLocalizedReportTypeTitle(string reportType)
    {
        return reportType switch
        {
            "BorrowerStatement" => L("BorrowerStatementTitle", "Borrower Statement"),
            "TransactionReport" => L("TransactionReportTitle", "Transaction Report"),
            "InterestReport" => L("InterestReportTitle", "Interest Report"),
            "OutstandingReport" => L("OutstandingReportTitle", "Outstanding Accounts Report"),
            "BorrowerSummary" => L("BorrowerSummaryTitle", "Borrower Portfolio Summary"),
            _ => L(reportType, reportType)
        };
    }

    public Task<string> ExportReportToPdfAsync(object report, string reportType, CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report), "Cannot export null report to PDF.");
        }

        var exportDir = Path.Combine(_pathService.ApplicationDataDirectory, "Exports");
        Directory.CreateDirectory(exportDir);

        var dateStr = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var fileName = $"DhirDhar_{reportType}_{dateStr}.pdf";
        var filePath = Path.Combine(exportDir, fileName);

        var reportTitle = GetLocalizedReportTypeTitle(reportType);
        var appTitle = L("ApplicationTitle", "DHIRDHAR FINANCIAL MANAGEMENT SYSTEM");

        using var document = new PdfDocument();
        document.Info.Title = $"DhirDhar - {reportTitle}";
        document.Info.Author = appTitle;

        var page = document.AddPage();
        page.Size = PageSize.A4;
        page.Orientation = PageOrientation.Landscape;

        using var gfx = XGraphics.FromPdfPage(page);
        var fontTitle = new XFont("Arial", 16, XFontStyle.Bold);
        var fontSub = new XFont("Arial", 10, XFontStyle.Regular);
        var fontHeader = new XFont("Arial", 10, XFontStyle.Bold);
        var fontTableHead = new XFont("Arial", 9, XFontStyle.Bold);
        var fontBody = new XFont("Arial", 8, XFontStyle.Regular);
        var fontBold = new XFont("Arial", 9, XFontStyle.Bold);

        double margin = 30;
        double pageWidth = page.Width.Point - (margin * 2);
        double yPos = 30;

        var genLabel = L("Generated", "Generated");
        var repLabel = L("Report", "Report");

        // Banner
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 30, 41, 59)), margin, yPos, pageWidth, 40);
        gfx.DrawString(appTitle, fontTitle, XBrushes.White, new XRect(margin + 12, yPos + 6, pageWidth, 20), XStringFormats.TopLeft);
        gfx.DrawString($"{repLabel}: {reportTitle}  |  {genLabel}: {FormatDate(DateTime.Now, true)}", fontSub, XBrushes.LightGray, new XRect(margin + 12, yPos + 24, pageWidth, 16), XStringFormats.TopLeft);

        yPos += 50;

        switch (report)
        {
            case BorrowerStatementReport stmt:
                yPos = DrawBorrowerStatement(gfx, page, document, stmt, fontHeader, fontSub, fontTableHead, fontBody, fontBold, margin, yPos, pageWidth);
                break;
            case TransactionReport txn:
                yPos = DrawTransactionReport(gfx, page, document, txn, fontHeader, fontSub, fontTableHead, fontBody, fontBold, margin, yPos, pageWidth);
                break;
            case InterestReport intr:
                yPos = DrawInterestReport(gfx, page, document, intr, fontHeader, fontSub, fontTableHead, fontBody, fontBold, margin, yPos, pageWidth);
                break;
            case OutstandingReport outst:
                yPos = DrawOutstandingReport(gfx, page, document, outst, fontHeader, fontSub, fontTableHead, fontBody, fontBold, margin, yPos, pageWidth);
                break;
            case BorrowerSummaryReport sum:
                yPos = DrawBorrowerSummary(gfx, page, document, sum, fontHeader, fontSub, fontTableHead, fontBody, fontBold, margin, yPos, pageWidth);
                break;
        }

        document.Save(filePath);
        _logger.LogInformation("PDF report created at {FilePath}", filePath);
        return Task.FromResult(filePath);
    }

    private double DrawBorrowerStatement(XGraphics gfx, PdfPage page, PdfDocument doc, BorrowerStatementReport stmt, XFont fHead, XFont fSub, XFont fTHead, XFont fBody, XFont fBold, double margin, double yPos, double pageWidth)
    {
        var bLabel = L("Borrower", "Borrower");
        var periodLabel = L("Period", "Period");
        var toLabel = L("To", "to");
        var statusLabel = L("Status", "Status");
        var rateLabel = L("Rate", "Rate");
        var perMonthLabel = L("PerMonth", "/ month");
        var statusVal = L(stmt.AccountStatus, stmt.AccountStatus);

        // Info Header
        gfx.DrawString($"{bLabel}: #{LocalizeDigits(stmt.BorrowerNumber)} - {LocalizeText(stmt.BorrowerName)} ({LocalizeDigits(stmt.Contact)})", fHead, XBrushes.Black, new XPoint(margin, yPos));
        gfx.DrawString($"{periodLabel}: {FormatDate(stmt.FromDate)} {toLabel} {FormatDate(stmt.ToDate)} | {statusLabel}: {statusVal} | {rateLabel}: {FormatPercent(stmt.InterestRate)} {perMonthLabel}", fSub, XBrushes.DarkGray, new XPoint(margin, yPos + 14));
        yPos += 30;

        // Table Header
        var headers = new[]
        {
            L("Date", "Date"),
            L("Type", "Type"),
            $"{L("Debit", "Debit")} (₹)",
            $"{L("Credit", "Credit")} (₹)",
            $"{L("Interest", "Interest")} (₹)",
            $"{L("Balance", "Balance")} (₹)",
            L("Description", "Description")
        };
        var colWidths = new[] { 75, 80, 95, 95, 95, 105, 237 };
        DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
        yPos += 20;

        foreach (var entry in stmt.FinancialHistory)
        {
            if (yPos > page.Height.Point - 50)
            {
                page = doc.AddPage();
                page.Size = PageSize.A4;
                page.Orientation = PageOrientation.Landscape;
                gfx = XGraphics.FromPdfPage(page);
                yPos = 30;
                DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
                yPos += 20;
            }

            var typeVal = L(entry.EventType, entry.EventType);
            var debitStr = entry.EventType.Equals("Withdrawal", StringComparison.OrdinalIgnoreCase) && entry.TransactionAmount.HasValue
                ? FormatCurrency(entry.TransactionAmount.Value)
                : "-";
            var creditStr = entry.EventType.Equals("Deposit", StringComparison.OrdinalIgnoreCase) && entry.TransactionAmount.HasValue
                ? FormatCurrency(entry.TransactionAmount.Value)
                : "-";
            var interestStr = entry.InterestAmount.HasValue && entry.InterestAmount.Value > 0
                ? FormatCurrency(entry.InterestAmount.Value)
                : "-";
            var descStr = !string.IsNullOrWhiteSpace(entry.Description) ? LocalizeText(entry.Description) : "-";

            gfx.DrawString(FormatDate(entry.Date), fBody, XBrushes.Black, new XPoint(margin + 5, yPos + 10));
            gfx.DrawString(typeVal, fBody, XBrushes.Black, new XPoint(margin + 80, yPos + 10));
            gfx.DrawString(debitStr, fBody, XBrushes.Black, new XPoint(margin + 160, yPos + 10));
            gfx.DrawString(creditStr, fBody, XBrushes.Black, new XPoint(margin + 255, yPos + 10));
            gfx.DrawString(interestStr, fBody, XBrushes.Black, new XPoint(margin + 350, yPos + 10));
            gfx.DrawString(FormatCurrency(entry.ClosingPrincipal), fBody, XBrushes.Black, new XPoint(margin + 445, yPos + 10));
            gfx.DrawString(descStr, fBody, XBrushes.Black, new XPoint(margin + 550, yPos + 10));

            gfx.DrawLine(XPens.LightGray, margin, yPos + 15, margin + pageWidth, yPos + 15);
            yPos += 16;
        }

        yPos += 10;
        gfx.DrawRectangle(XBrushes.AliceBlue, margin, yPos, pageWidth, 25);
        var totalsStr = $"{L("Totals", "Totals")}:  {L("Opening", "Opening")}: {FormatCurrency(stmt.OpeningPrincipal)}   |   {L("TotalDeposits", "Deposits")}: {FormatCurrency(stmt.TotalDeposits)}   |   {L("TotalWithdrawals", "Withdrawals")}: {FormatCurrency(stmt.TotalWithdrawals)}   |   {L("AccruedInterest", "Accrued Interest")}: {FormatCurrency(stmt.TotalInterest)}   |   {L("FinalOutstanding", "Final Outstanding")}: {FormatCurrency(stmt.FinalOutstanding)}";
        gfx.DrawString(totalsStr, fBold, XBrushes.DarkSlateGray, new XRect(margin + 10, yPos + 5, pageWidth, 18), XStringFormats.TopLeft);
        return yPos + 35;
    }

    private double DrawTransactionReport(XGraphics gfx, PdfPage page, PdfDocument doc, TransactionReport txn, XFont fHead, XFont fSub, XFont fTHead, XFont fBody, XFont fBold, double margin, double yPos, double pageWidth)
    {
        var targetLabel = L("Borrower", "Target Borrower");
        var filterLabel = L("Filter", "Filter");
        var periodLabel = L("Period", "Period");
        var toLabel = L("To", "to");
        var bName = LocalizeText(txn.BorrowerName);
        var filterVal = L(txn.TransactionTypeFilter, txn.TransactionTypeFilter);

        gfx.DrawString($"{targetLabel}: {bName}  |  {filterLabel}: {filterVal}", fHead, XBrushes.Black, new XPoint(margin, yPos));
        gfx.DrawString($"{periodLabel}: {FormatDate(txn.FromDate)} {toLabel} {FormatDate(txn.ToDate)}", fSub, XBrushes.DarkGray, new XPoint(margin, yPos + 14));
        yPos += 30;

        var headers = new[]
        {
            L("Date", "Date"),
            L("BorrowerNumberColumn", "Borrower #"),
            L("BorrowerNameColumn", "Borrower Name"),
            L("Type", "Type"),
            $"{L("Amount", "Amount")} (₹)",
            $"{L("RunningBal", "Running Bal")} (₹)",
            L("Description", "Description")
        };
        var colWidths = new[] { 75, 85, 150, 80, 95, 105, 192 };
        DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
        yPos += 20;

        foreach (var item in txn.Items)
        {
            if (yPos > page.Height.Point - 50)
            {
                page = doc.AddPage();
                page.Size = PageSize.A4;
                page.Orientation = PageOrientation.Landscape;
                gfx = XGraphics.FromPdfPage(page);
                yPos = 30;
                DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
                yPos += 20;
            }

            var typeVal = L(item.Type, item.Type);
            var descStr = !string.IsNullOrWhiteSpace(item.Description) ? LocalizeText(item.Description) : "-";

            gfx.DrawString(FormatDate(item.Date), fBody, XBrushes.Black, new XPoint(margin + 5, yPos + 10));
            gfx.DrawString(LocalizeDigits(item.BorrowerNumber), fBody, XBrushes.Black, new XPoint(margin + 80, yPos + 10));
            gfx.DrawString(LocalizeText(item.BorrowerName), fBody, XBrushes.Black, new XPoint(margin + 165, yPos + 10));
            gfx.DrawString(typeVal, fBody, XBrushes.Black, new XPoint(margin + 315, yPos + 10));
            gfx.DrawString(FormatCurrency(item.Amount), fBody, XBrushes.Black, new XPoint(margin + 395, yPos + 10));
            gfx.DrawString(FormatCurrency(item.BalanceAfter), fBody, XBrushes.Black, new XPoint(margin + 490, yPos + 10));
            gfx.DrawString(descStr, fBody, XBrushes.Black, new XPoint(margin + 595, yPos + 10));

            gfx.DrawLine(XPens.LightGray, margin, yPos + 15, margin + pageWidth, yPos + 15);
            yPos += 16;
        }

        yPos += 10;
        gfx.DrawRectangle(XBrushes.AliceBlue, margin, yPos, pageWidth, 25);
        var totalsStr = $"{L("Totals", "Totals")}:  {L("TotalDeposits", "Total Deposits")}: {FormatCurrency(txn.TotalDeposits)}   |   {L("TotalWithdrawals", "Total Withdrawals")}: {FormatCurrency(txn.TotalWithdrawals)}   |   {L("NetPosition", "Net Amount")}: {FormatCurrency(txn.NetAmount)}";
        gfx.DrawString(totalsStr, fBold, XBrushes.DarkSlateGray, new XRect(margin + 10, yPos + 5, pageWidth, 18), XStringFormats.TopLeft);
        return yPos + 35;
    }

    private double DrawInterestReport(XGraphics gfx, PdfPage page, PdfDocument doc, InterestReport intr, XFont fHead, XFont fSub, XFont fTHead, XFont fBody, XFont fBold, double margin, double yPos, double pageWidth)
    {
        var targetLabel = L("Borrower", "Target");
        var statusLabel = L("Status", "Status");
        var calcPeriodLabel = L("CalculationPeriod", "Calculation Period");
        var toLabel = L("To", "to");
        var bName = LocalizeText(intr.BorrowerName);
        var statusVal = L(intr.AccountStatus, intr.AccountStatus);

        gfx.DrawString($"{targetLabel}: {bName}  |  {statusLabel}: {statusVal}", fHead, XBrushes.Black, new XPoint(margin, yPos));
        gfx.DrawString($"{calcPeriodLabel}: {FormatDate(intr.CalculationStart)} {toLabel} {FormatDate(intr.CalculationEnd)}", fSub, XBrushes.DarkGray, new XPoint(margin, yPos + 14));
        yPos += 30;

        var headers = new[]
        {
            L("StartDate", "Start Date"),
            L("EndDate", "End Date"),
            L("BorrowerNameColumn", "Borrower Name"),
            $"{L("OpeningPrincipal", "Opening Principal")} (₹)",
            $"{L("Rate", "Rate")} %",
            L("Days", "Days"),
            $"{L("AccruedInterest", "Accrued Interest")} (₹)",
            $"{L("ClosingPrincipal", "Closing Principal")} (₹)"
        };
        var colWidths = new[] { 75, 75, 140, 110, 50, 45, 115, 172 };
        DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
        yPos += 20;

        foreach (var seg in intr.Segments)
        {
            if (yPos > page.Height.Point - 50)
            {
                page = doc.AddPage();
                page.Size = PageSize.A4;
                page.Orientation = PageOrientation.Landscape;
                gfx = XGraphics.FromPdfPage(page);
                yPos = 30;
                DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
                yPos += 20;
            }

            gfx.DrawString(FormatDate(seg.StartDate), fBody, XBrushes.Black, new XPoint(margin + 5, yPos + 10));
            gfx.DrawString(FormatDate(seg.EndDate), fBody, XBrushes.Black, new XPoint(margin + 80, yPos + 10));
            gfx.DrawString(LocalizeText(seg.BorrowerName), fBody, XBrushes.Black, new XPoint(margin + 155, yPos + 10));
            gfx.DrawString(FormatCurrency(seg.OpeningPrincipal), fBody, XBrushes.Black, new XPoint(margin + 295, yPos + 10));
            gfx.DrawString(FormatPercent(seg.Rate), fBody, XBrushes.Black, new XPoint(margin + 405, yPos + 10));
            gfx.DrawString(LocalizeDigits(seg.Days.ToString()), fBody, XBrushes.Black, new XPoint(margin + 455, yPos + 10));
            gfx.DrawString(FormatCurrency(seg.Interest), fBody, XBrushes.Black, new XPoint(margin + 500, yPos + 10));
            gfx.DrawString(FormatCurrency(seg.ClosingPrincipal ?? 0m), fBody, XBrushes.Black, new XPoint(margin + 615, yPos + 10));

            gfx.DrawLine(XPens.LightGray, margin, yPos + 15, margin + pageWidth, yPos + 15);
            yPos += 16;
        }

        yPos += 10;
        gfx.DrawRectangle(XBrushes.AliceBlue, margin, yPos, pageWidth, 25);
        var totalsStr = $"{L("Totals", "Totals")}:  {L("OpeningPrincipal", "Opening Principal")}: {FormatCurrency(intr.OpeningPrincipal)}   |   {L("TotalAccruedInterest", "Total Accrued Interest")}: {FormatCurrency(intr.TotalInterest)}   |   {L("ClosingPrincipal", "Closing Principal")}: {FormatCurrency(intr.ClosingPrincipal)}";
        gfx.DrawString(totalsStr, fBold, XBrushes.DarkSlateGray, new XRect(margin + 10, yPos + 5, pageWidth, 18), XStringFormats.TopLeft);
        return yPos + 35;
    }

    private double DrawOutstandingReport(XGraphics gfx, PdfPage page, PdfDocument doc, OutstandingReport outst, XFont fHead, XFont fSub, XFont fTHead, XFont fBody, XFont fBold, double margin, double yPos, double pageWidth)
    {
        var reportTitle = L("OutstandingReportTitle", "Outstanding Accounts Report");
        var borrowersListedLabel = L("BorrowersListed", "Borrowers Listed");
        var genDateLabel = L("GeneratedDate", "Generated Date");

        gfx.DrawString($"{reportTitle} - {LocalizeDigits(outst.Items.Count.ToString())} {borrowersListedLabel}", fHead, XBrushes.Black, new XPoint(margin, yPos));
        gfx.DrawString($"{genDateLabel}: {FormatDate(outst.GeneratedDate, true)}", fSub, XBrushes.DarkGray, new XPoint(margin, yPos + 14));
        yPos += 30;

        var headers = new[]
        {
            L("BorrowerNumberColumn", "Borrower #"),
            L("BorrowerNameColumn", "Borrower Name"),
            L("ContactColumn", "Contact"),
            $"{L("Principal", "Principal")} (₹)",
            $"{L("AccruedInterest", "Accrued Interest")} (₹)",
            $"{L("TotalOutstanding", "Total Outstanding")} (₹)",
            L("Status", "Status"),
            L("LastActivity", "Last Activity")
        };
        var colWidths = new[] { 85, 140, 95, 105, 110, 115, 65, 67 };
        DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
        yPos += 20;

        foreach (var item in outst.Items)
        {
            if (yPos > page.Height.Point - 50)
            {
                page = doc.AddPage();
                page.Size = PageSize.A4;
                page.Orientation = PageOrientation.Landscape;
                gfx = XGraphics.FromPdfPage(page);
                yPos = 30;
                DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
                yPos += 20;
            }

            var statusVal = L(item.Status, item.Status);
            var lastAct = item.LastActivityDate.HasValue ? FormatDate(item.LastActivityDate.Value) : "-";

            gfx.DrawString(LocalizeDigits(item.BorrowerNumber), fBody, XBrushes.Black, new XPoint(margin + 5, yPos + 10));
            gfx.DrawString(LocalizeText(item.BorrowerName), fBody, XBrushes.Black, new XPoint(margin + 90, yPos + 10));
            gfx.DrawString(LocalizeDigits(item.Contact), fBody, XBrushes.Black, new XPoint(margin + 230, yPos + 10));
            gfx.DrawString(FormatCurrency(item.Principal), fBody, XBrushes.Black, new XPoint(margin + 325, yPos + 10));
            gfx.DrawString(FormatCurrency(item.AccumulatedInterest), fBody, XBrushes.Black, new XPoint(margin + 430, yPos + 10));
            gfx.DrawString(FormatCurrency(item.Outstanding), fBody, XBrushes.Black, new XPoint(margin + 540, yPos + 10));
            gfx.DrawString(statusVal, fBody, XBrushes.Black, new XPoint(margin + 655, yPos + 10));
            gfx.DrawString(lastAct, fBody, XBrushes.Black, new XPoint(margin + 720, yPos + 10));

            gfx.DrawLine(XPens.LightGray, margin, yPos + 15, margin + pageWidth, yPos + 15);
            yPos += 16;
        }

        yPos += 10;
        gfx.DrawRectangle(XBrushes.AliceBlue, margin, yPos, pageWidth, 25);
        var totalsStr = $"{L("GrandTotals", "Grand Totals")}:  {L("TotalPrincipal", "Total Principal")}: {FormatCurrency(outst.TotalPrincipal)}   |   {L("TotalAccruedInterest", "Total Accrued Interest")}: {FormatCurrency(outst.TotalInterest)}   |   {L("GrandTotalOutstanding", "Grand Total Outstanding")}: {FormatCurrency(outst.TotalOutstanding)}";
        gfx.DrawString(totalsStr, fBold, XBrushes.DarkSlateGray, new XRect(margin + 10, yPos + 5, pageWidth, 18), XStringFormats.TopLeft);
        return yPos + 35;
    }

    private double DrawBorrowerSummary(XGraphics gfx, PdfPage page, PdfDocument doc, BorrowerSummaryReport sum, XFont fHead, XFont fSub, XFont fTHead, XFont fBody, XFont fBold, double margin, double yPos, double pageWidth)
    {
        var summaryTitle = L("BorrowerSummaryTitle", "Borrower Portfolio Summary");
        var totalLabel = L("Total", "Total");
        var activeLabel = L("Active", "Active");
        var inactiveLabel = L("Inactive", "Inactive");
        var closedLabel = L("Closed", "Closed");
        var genDateLabel = L("GeneratedDate", "Generated Date");

        gfx.DrawString($"{summaryTitle} - {totalLabel}: {LocalizeDigits(sum.TotalBorrowers.ToString())} ({activeLabel}: {LocalizeDigits(sum.ActiveBorrowers.ToString())}, {inactiveLabel}: {LocalizeDigits(sum.InactiveBorrowers.ToString())}, {closedLabel}: {LocalizeDigits(sum.ClosedBorrowers.ToString())})", fHead, XBrushes.Black, new XPoint(margin, yPos));
        gfx.DrawString($"{genDateLabel}: {FormatDate(sum.GeneratedDate, true)}", fSub, XBrushes.DarkGray, new XPoint(margin, yPos + 14));
        yPos += 30;

        var headers = new[]
        {
            L("BorrowerNumberColumn", "Borrower #"),
            L("BorrowerNameColumn", "Borrower Name"),
            $"{L("Withdrawn", "Withdrawn")} (₹)",
            $"{L("Deposited", "Deposited")} (₹)",
            $"{L("Interest", "Interest")} (₹)",
            $"{L("CurrentBal", "Current Bal")} (₹)",
            $"{L("TotalOutstanding", "Outstanding")} (₹)",
            L("Status", "Status")
        };
        var colWidths = new[] { 80, 130, 95, 95, 90, 95, 115, 82 };
        DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
        yPos += 20;

        foreach (var item in sum.Items)
        {
            if (yPos > page.Height.Point - 50)
            {
                page = doc.AddPage();
                page.Size = PageSize.A4;
                page.Orientation = PageOrientation.Landscape;
                gfx = XGraphics.FromPdfPage(page);
                yPos = 30;
                DrawTableHeader(gfx, margin, yPos, pageWidth, fTHead, headers, colWidths);
                yPos += 20;
            }

            var statusVal = L(item.Status, item.Status);

            gfx.DrawString(LocalizeDigits(item.BorrowerNumber), fBody, XBrushes.Black, new XPoint(margin + 5, yPos + 10));
            gfx.DrawString(LocalizeText(item.BorrowerName), fBody, XBrushes.Black, new XPoint(margin + 85, yPos + 10));
            gfx.DrawString(FormatCurrency(item.TotalWithdrawn), fBody, XBrushes.Black, new XPoint(margin + 215, yPos + 10));
            gfx.DrawString(FormatCurrency(item.TotalDeposited), fBody, XBrushes.Black, new XPoint(margin + 310, yPos + 10));
            gfx.DrawString(FormatCurrency(item.TotalInterest), fBody, XBrushes.Black, new XPoint(margin + 405, yPos + 10));
            gfx.DrawString(FormatCurrency(item.CurrentBalance), fBody, XBrushes.Black, new XPoint(margin + 495, yPos + 10));
            gfx.DrawString(FormatCurrency(item.TotalOutstanding), fBody, XBrushes.Black, new XPoint(margin + 590, yPos + 10));
            gfx.DrawString(statusVal, fBody, XBrushes.Black, new XPoint(margin + 705, yPos + 10));

            gfx.DrawLine(XPens.LightGray, margin, yPos + 15, margin + pageWidth, yPos + 15);
            yPos += 16;
        }

        yPos += 10;
        gfx.DrawRectangle(XBrushes.AliceBlue, margin, yPos, pageWidth, 25);
        var totalsStr = $"{L("GrandTotals", "Grand Totals")}:  {L("TotalDeposited", "Total Deposited")}: {FormatCurrency(sum.TotalDeposits)}   |   {L("TotalWithdrawn", "Total Withdrawn")}: {FormatCurrency(sum.TotalWithdrawals)}   |   {L("TotalAccruedInterest", "Total Accrued Interest")}: {FormatCurrency(sum.TotalInterest)}   |   {L("GrandTotalOutstanding", "Grand Total Outstanding")}: {FormatCurrency(sum.TotalOutstanding)}";
        gfx.DrawString(totalsStr, fBold, XBrushes.DarkSlateGray, new XRect(margin + 10, yPos + 5, pageWidth, 18), XStringFormats.TopLeft);
        return yPos + 35;
    }

    private static void DrawTableHeader(XGraphics gfx, double margin, double yPos, double pageWidth, XFont fTHead, string[] headers, int[] widths)
    {
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 30, 41, 59)), margin, yPos, pageWidth, 20);
        double x = margin + 5;
        for (int i = 0; i < headers.Length && i < widths.Length; i++)
        {
            gfx.DrawString(headers[i], fTHead, XBrushes.White, new XPoint(x, yPos + 13));
            x += widths[i];
        }
    }
}
