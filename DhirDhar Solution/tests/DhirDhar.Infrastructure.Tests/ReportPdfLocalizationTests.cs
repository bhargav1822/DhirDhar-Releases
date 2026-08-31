using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Ledger.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Reports.Models;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class ReportPdfLocalizationTests : IDisposable
{
    private readonly string _testDir;
    private readonly IDatabasePathService _pathService;
    private readonly ILocalizationService _localizationService;
    private readonly ITranslationService _translationService;
    private readonly PdfExportService _pdfExportService;

    public ReportPdfLocalizationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "DhirDhar_ReportPdfTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _pathService = new TestDatabasePathService(_testDir);
        _localizationService = new LocalizationService();
        _translationService = new TestTranslationService();

        _pdfExportService = new PdfExportService(
            _pathService,
            NullLogger<PdfExportService>.Instance,
            _localizationService,
            _translationService);
    }

    private sealed class TestTranslationService : ITranslationService
    {
        public string Translate(string? text, string targetLanguageCode) =>
            string.IsNullOrWhiteSpace(text) ? string.Empty : ScriptTranslator.Translate(text, targetLanguageCode);

        public Task<string> TranslateAsync(string? text, string targetLanguageCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Translate(text, targetLanguageCode));

        public string DetectLanguage(string? text) => ScriptTranslator.DetectLanguage(text);

        public Task InvalidateTranslationsAsync(string oldText, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PreloadTranslationsAsync(IEnumerable<string> texts, string targetLanguageCode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetTranslationAsync(string sourceText, string targetLanguageCode, string translatedText, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestDatabasePathService : IDatabasePathService
    {
        public TestDatabasePathService(string dir)
        {
            ApplicationDataDirectory = dir;
            DatabaseDirectory = dir;
            DatabasePath = Path.Combine(dir, "DhirDhar.db");
            BackupDirectory = Path.Combine(dir, "Backups");
            LogDirectory = Path.Combine(dir, "Logs");
        }

        public string ApplicationDataDirectory { get; }
        public string DatabaseDirectory { get; }
        public string DatabasePath { get; }
        public string BackupDirectory { get; }
        public string LogDirectory { get; }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task ExportReportToPdfAsync_BorrowerStatement_InGujarati_GeneratesValidPdf()
    {
        _localizationService.SetLanguage("gu-IN");

        var entries = new List<LedgerEntryDto>
        {
            new LedgerEntryDto(
                DateTime.Today.AddDays(-30),
                "Withdrawal",
                "Gold Loan Disbursed",
                50000m,
                null,
                3.0m,
                0m,
                50000m,
                "DOC-001",
                "Completed"),
            new LedgerEntryDto(
                DateTime.Today.AddDays(-10),
                "Deposit",
                "Partial Payment Received",
                10000m,
                1500m,
                3.0m,
                50000m,
                40000m,
                "DOC-002",
                "Completed")
        };

        var stmt = new BorrowerStatementReport(
            "DJ01",
            "Ramesh Patel",
            "9876543210",
            "Active",
            DateTime.Today.AddDays(-60),
            null,
            3.0m,
            DateTime.Today.AddDays(-30),
            DateTime.Today,
            50000m,
            10000m,
            0m,
            1500m,
            41500m,
            entries);

        var filePath = await _pdfExportService.ExportReportToPdfAsync(stmt, "BorrowerStatement");

        Assert.True(File.Exists(filePath));
        var fileInfo = new FileInfo(filePath);
        Assert.True(fileInfo.Length > 0);
    }

    [Fact]
    public async Task ExportReportToPdfAsync_TransactionReport_InGujarati_GeneratesValidPdf()
    {
        _localizationService.SetLanguage("gu-IN");

        var items = new List<TransactionReportItem>
        {
            new TransactionReportItem(
                DateTime.Today.AddDays(-5),
                "DJ01",
                "Ramesh Patel",
                "Withdrawal",
                25000m,
                25000m,
                "Cash Loan Disbursed"),
            new TransactionReportItem(
                DateTime.Today,
                "DJ02",
                "Suresh Shah",
                "Deposit",
                5000m,
                20000m,
                "Interest Payment")
        };

        var txn = new TransactionReport(
            DateTime.Today.AddMonths(-1),
            DateTime.Today,
            "All",
            "All Borrowers",
            items,
            5000m,
            25000m,
            -20000m);

        var filePath = await _pdfExportService.ExportReportToPdfAsync(txn, "TransactionReport");

        Assert.True(File.Exists(filePath));
        Assert.True(new FileInfo(filePath).Length > 0);
    }

    [Fact]
    public async Task ExportReportToPdfAsync_InterestReport_InGujarati_GeneratesValidPdf()
    {
        _localizationService.SetLanguage("gu-IN");

        var segments = new List<InterestReportSegment>
        {
            new InterestReportSegment(
                DateTime.Today.AddMonths(-2),
                DateTime.Today.AddMonths(-1),
                "Ramesh Patel",
                50000m,
                3.0m,
                30,
                30,
                1500m,
                "Withdrawal",
                50000m)
        };

        var intr = new InterestReport(
            Guid.NewGuid(),
            "Ramesh Patel",
            DateTime.Today.AddMonths(-2),
            DateTime.Today,
            50000m,
            50000m,
            1500m,
            "Active",
            null,
            segments);

        var filePath = await _pdfExportService.ExportReportToPdfAsync(intr, "InterestReport");

        Assert.True(File.Exists(filePath));
        Assert.True(new FileInfo(filePath).Length > 0);
    }

    [Fact]
    public async Task ExportReportToPdfAsync_OutstandingReport_InGujarati_GeneratesValidPdf()
    {
        _localizationService.SetLanguage("gu-IN");

        var items = new List<OutstandingReportItem>
        {
            new OutstandingReportItem(
                "DJ01",
                "Ramesh Patel",
                "9876543210",
                50000m,
                3000m,
                53000m,
                "Active",
                DateTime.Today.AddDays(-3)),
            new OutstandingReportItem(
                "DJ02",
                "Suresh Shah",
                "9123456780",
                20000m,
                600m,
                20600m,
                "Active",
                DateTime.Today.AddDays(-1))
        };

        var outst = new OutstandingReport(
            DateTime.Now,
            items,
            70000m,
            3600m,
            73600m);

        var filePath = await _pdfExportService.ExportReportToPdfAsync(outst, "OutstandingReport");

        Assert.True(File.Exists(filePath));
        Assert.True(new FileInfo(filePath).Length > 0);
    }

    [Fact]
    public async Task ExportReportToPdfAsync_BorrowerSummary_InEnglish_GeneratesValidPdf()
    {
        _localizationService.SetLanguage("en-IN");

        var items = new List<BorrowerSummaryItem>
        {
            new BorrowerSummaryItem(
                "DJ01",
                "Ramesh Patel",
                "9876543210",
                50000m,
                10000m,
                3000m,
                40000m,
                43000m,
                "Active",
                DateTime.Today.AddDays(-2)),
            new BorrowerSummaryItem(
                "DJ02",
                "Suresh Shah",
                "9123456780",
                20000m,
                5000m,
                600m,
                15000m,
                15600m,
                "Active",
                DateTime.Today.AddDays(-5))
        };

        var sum = new BorrowerSummaryReport(
            DateTime.Now,
            2,
            2,
            0,
            0,
            items,
            15000m,
            70000m,
            3600m,
            58600m);

        var filePath = await _pdfExportService.ExportReportToPdfAsync(sum, "BorrowerSummary");

        Assert.True(File.Exists(filePath));
        Assert.True(new FileInfo(filePath).Length > 0);
    }
}
