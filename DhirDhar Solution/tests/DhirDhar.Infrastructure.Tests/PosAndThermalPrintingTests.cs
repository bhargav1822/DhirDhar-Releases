using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Printing;
using DhirDhar.Application.QrCode;
using DhirDhar.Application.Settings;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Printing;
using DhirDhar.Infrastructure.QrCode;
using DhirDhar.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class PosAndThermalPrintingTests : IDisposable
{
    private readonly string _testDir;
    private readonly IDatabasePathService _pathService;

    public PosAndThermalPrintingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "DhirDhar_PrintTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _pathService = new TestDatabasePathService(_testDir);
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
        catch { }
    }

    [Theory]
    [InlineData("A4", 595.27, 841.88, false)]
    [InlineData("A5", 419.52, 595.27, false)]
    [InlineData("Letter", 612.0, 792.0, false)]
    [InlineData("POS 58 mm", 164.4, 500.0, true)]
    [InlineData("POS 80 mm", 226.77, 500.0, true)]
    [InlineData("POS 110 mm", 311.81, 500.0, true)]
    [InlineData("POS Custom", 255.11, 500.0, true)] // 90 mm
    public void PaperSizeHelper_CalculatesCorrectDimensions(string code, double expectedWidth, double expectedHeight, bool isContinuous)
    {
        var (w, h, cont) = PaperSizeHelper.GetDimensions(code, customWidthMm: 90.0, defaultContinuousHeightPt: 500.0);

        Assert.Equal(expectedWidth, w, 0.5);
        Assert.Equal(expectedHeight, h, 0.5);
        Assert.Equal(isContinuous, cont);
    }

    [Theory]
    [InlineData("POS 58 mm", true)]
    [InlineData("POS 80 mm", true)]
    [InlineData("POS 110 mm", true)]
    [InlineData("POS Custom", true)]
    [InlineData("A4", false)]
    [InlineData("A5", false)]
    [InlineData("Letter", false)]
    public void PaperSizeHelper_IsThermalPosSize_IdentifiesCorrectly(string code, bool expectedThermal)
    {
        Assert.Equal(expectedThermal, PaperSizeHelper.IsThermalPosSize(code));
    }

    [Fact]
    public void PosReceiptBuilder_GeneratesValid58mmBorrowerReceipt()
    {
        var receipt = new ReceiptData
        {
            Type = ReceiptType.BorrowerReceipt,
            BusinessName = "ધીરધાર ફાઇનાન્સ",
            Title = "ખાતેદાર રસીદ",
            BorrowerName = "ભાર્ગવ પરીખ",
            BorrowerNumber = "DJ01",
            Contact = "9876543210",
            Village = "અમદાવાદ",
            LoanDate = new DateTime(2026, 8, 21),
            InitialPrincipal = 10000m,
            InterestRate = 3.00m,
            DisplayDuration = "12 Months",
            MonthlyInterest = 300m,
            CurrentPrincipal = 10000m,
            TotalInterest = 300m,
            TotalOutstanding = 10300m,
            PaperSize = "POS58",
            LanguageCode = "gu-IN",
            FooterNote = "આભાર"
        };

        var filePath = PosReceiptBuilder.BuildReceiptPdf(receipt, _testDir);

        Assert.True(File.Exists(filePath));
        var fileInfo = new FileInfo(filePath);
        Assert.True(fileInfo.Length > 0);

        using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.True(doc.PageCount >= 1);
        Assert.Equal(164.4, doc.Pages[0].Width.Point, 1.0);
    }

    [Fact]
    public void PosReceiptBuilder_GeneratesValid80mmDepositReceipt_WithQrCode()
    {
        var qrService = new QrCodeService();
        var qrBytes = qrService.GeneratePngBytes("DJ01", 10);

        var receipt = new ReceiptData
        {
            Type = ReceiptType.ReceiveAmount,
            BusinessName = "DhirDhar Finance",
            Title = "Deposit Receipt",
            BorrowerName = "Palak Shah",
            BorrowerNumber = "DJ02",
            Contact = "9898989898",
            TransactionDate = new DateTime(2026, 8, 21),
            TransactionType = "Deposit",
            TransactionAmount = 5000m,
            PaymentMode = "Cash",
            CurrentPrincipal = 5000m,
            TotalOutstanding = 5000m,
            QrCodePayload = qrService.FormatPayload("DJ02"),
            QrCodePngBytes = qrBytes,
            PaperSize = "POS80",
            LanguageCode = "en-IN",
            FooterNote = "Thank you for your payment!"
        };

        var filePath = PosReceiptBuilder.BuildReceiptPdf(receipt, _testDir);

        Assert.True(File.Exists(filePath));
        using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.True(doc.PageCount >= 1);
        Assert.Equal(226.77, doc.Pages[0].Width.Point, 1.0);
    }

    [Fact]
    public void PosReceiptBuilder_GeneratesValidGiveAmountReceipt_WithJewelleryDetails()
    {
        var receipt = new ReceiptData
        {
            Type = ReceiptType.GiveAmount,
            BusinessName = "ધીરધાર",
            BorrowerName = "મનન પટેલ",
            BorrowerNumber = "DJ03",
            TransactionDate = new DateTime(2026, 8, 20),
            TransactionType = "Withdrawal",
            TransactionAmount = 25000m,
            OrnamentType = "લોકેટ (Locket)",
            OrnamentWeight = "19.00 ગ્રામ",
            CurrentPrincipal = 25000m,
            TotalOutstanding = 25750m,
            PaperSize = "POS80",
            LanguageCode = "gu-IN"
        };

        var filePath = PosReceiptBuilder.BuildReceiptPdf(receipt, _testDir);

        Assert.True(File.Exists(filePath));
        using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.True(doc.PageCount >= 1);
    }

    [Fact]
    public void PosReceiptBuilder_GeneratesAccountStatement_WithMultipleHistoryRows()
    {
        var receipt = new ReceiptData
        {
            Type = ReceiptType.AccountStatement,
            BusinessName = "DhirDhar Systems",
            BorrowerName = "Kamal Vyas",
            BorrowerNumber = "DJ04",
            LoanDate = new DateTime(2026, 1, 1),
            InitialPrincipal = 50000m,
            CurrentPrincipal = 30000m,
            TotalInterest = 1500m,
            TotalOutstanding = 31500m,
            PaperSize = "POS80",
            LanguageCode = "en-IN",
            Items = new List<ReceiptItemRow>
            {
                new(new DateTime(2026, 1, 1), "Loan Given", 50000m, null, null, 50000m, "Opening Loan"),
                new(new DateTime(2026, 2, 1), "Interest", null, null, 1500m, 51500m, "Jan Interest"),
                new(new DateTime(2026, 2, 15), "Deposit", null, 20000m, null, 31500m, "Partial Payment")
            }
        };

        var filePath = PosReceiptBuilder.BuildReceiptPdf(receipt, _testDir);

        Assert.True(File.Exists(filePath));
        using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.True(doc.PageCount >= 1);
    }

    [Fact]
    public void PosReceiptBuilder_ReflowsLongTextAndMultiLineDescriptions_WithoutCrashing()
    {
        var receipt = new ReceiptData
        {
            Type = ReceiptType.Transaction,
            BusinessName = "ધીરધાર ફાઇનાન્સિયલ્સ અમદાવાદ શાખા",
            BorrowerName = "ભાર્ગવકુમાર અશ્વિનભાઈ પટેલ-પંચાલ (મોટો ખાતેદાર)",
            BorrowerNumber = "DJ9999",
            Description = "આ ખાતામાં સોનાની ચેઇન તથા કાનની બુટ્ટીઓ ગીરવે મુકેલ છે અને નિયમિત માસિક હપ્તો રોકડેથી ચૂકવવા જણાવેલ છે.",
            TransactionDate = DateTime.Now,
            TransactionAmount = 150000m,
            PaperSize = "POS58", // Narrow 58mm test
            LanguageCode = "gu-IN"
        };

        var filePath = PosReceiptBuilder.BuildReceiptPdf(receipt, _testDir);

        Assert.True(File.Exists(filePath));
        using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.True(doc.PageCount >= 1);
        Assert.Equal(164.4, doc.Pages[0].Width.Point, 1.0);
    }

    [Fact]
    public void PosReceiptBuilder_SupportsA4StandardPagePreservation()
    {
        var receipt = new ReceiptData
        {
            Type = ReceiptType.LoanSummary,
            BusinessName = "DhirDhar",
            BorrowerName = "Ramesh Kumar",
            BorrowerNumber = "DJ10",
            InitialPrincipal = 100000m,
            InterestRate = 2.5m,
            PaperSize = "A4",
            LanguageCode = "en-IN"
        };

        var filePath = PosReceiptBuilder.BuildReceiptPdf(receipt, _testDir);

        Assert.True(File.Exists(filePath));
        using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        Assert.Equal(595.27, doc.Pages[0].Width.Point, 1.0);
        Assert.Equal(841.88, doc.Pages[0].Height.Point, 1.0);
    }

    [Fact]
    public async Task SettingsService_PersistsAndLoads_PrintingSettings()
    {
        using var temp = new Persistence.TempDatabase();
        var dbContext = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await dbContext.Database.EnsureCreatedAsync();

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(dbContext);
        var provider = services.BuildServiceProvider();

        var scopeFactory = provider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var localization = new LocalizationService();
        var dateLocalization = new DateLocalizationService();

        var settingsService = new SettingsService(scopeFactory, localization, dateLocalization, NullLogger<SettingsService>.Instance);

        var model = await settingsService.GetSettingsAsync();
        model.PaperSize = "POS80";
        model.CustomPaperWidthMm = 82.5;
        model.AutoCutPaper = true;
        model.SelectedPrinter = "POS-80-Thermal-Printer";

        await settingsService.SaveSettingsAsync(model);

        var loaded = await settingsService.GetSettingsAsync();
        Assert.Equal("POS80", loaded.PaperSize);
        Assert.Equal(82.5, loaded.CustomPaperWidthMm);
        Assert.True(loaded.AutoCutPaper);
        Assert.Equal("POS-80-Thermal-Printer", loaded.SelectedPrinter);
    }

    [Fact]
    public async Task WindowsPrinterService_GeneratesReceiptPdfAsync_Successfully()
    {
        var printerService = new WindowsPrinterService(_pathService, NullLogger<WindowsPrinterService>.Instance);

        var receipt = new ReceiptData
        {
            Type = ReceiptType.BorrowerQrCode,
            BusinessName = "DhirDhar",
            BorrowerName = "Palak",
            BorrowerNumber = "DJ11",
            PaperSize = "POS80",
            LanguageCode = "gu-IN"
        };

        var path = await printerService.GenerateReceiptPdfAsync(receipt);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void WindowsPrinterService_GetInstalledPrinters_ReturnsNonEmptyList()
    {
        var printerService = new WindowsPrinterService(_pathService, NullLogger<WindowsPrinterService>.Instance);
        var printers = printerService.GetInstalledPrinters();

        Assert.NotNull(printers);
        // Returns actual installed printers on Windows system
        if (OperatingSystem.IsWindows())
        {
            Assert.NotEmpty(printers);
        }
    }

    [Fact]
    public void WindowsPrinterService_GetSupportedPaperSizes_ReturnsStandardAndContinuousSizes()
    {
        var printerService = new WindowsPrinterService(_pathService, NullLogger<WindowsPrinterService>.Instance);
        var sizes = printerService.GetSupportedPaperSizes();

        Assert.NotNull(sizes);
        Assert.NotEmpty(sizes);
        Assert.All(sizes, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.DisplayLabel));
            Assert.True(s.WidthMm >= 0);
            Assert.True(s.HeightMm >= 0);
        });
    }

    [Theory]
    [InlineData("POS-80 Thermal Printer", true)]
    [InlineData("EPSON TM-T20III Receipt", true)]
    [InlineData("Star TSP100 Cutter", true)]
    [InlineData("XP-58 USB Receipt Printer", true)]
    [InlineData("Rongta 80mm POS", true)]
    [InlineData("Microsoft Print to PDF", false)]
    [InlineData("HP LaserJet Pro MFP M428fdw", false)]
    [InlineData("Canon PIXMA G3000", false)]
    public void WindowsPrinterService_IsThermalPrinter_DetectsThermalVsDesktopPrinters(string printerName, bool expectedThermal)
    {
        var printerService = new WindowsPrinterService(_pathService, NullLogger<WindowsPrinterService>.Instance);
        var isThermal = printerService.IsThermalPrinter(printerName);

        Assert.Equal(expectedThermal, isThermal);
    }

    [Fact]
    public async Task WindowsPrinterService_PrintTestReceiptAsync_ThrowsOnInvalidOrMissingPrinter()
    {
        var locService = new LocalizationService();
        var printerService = new WindowsPrinterService(_pathService, NullLogger<WindowsPrinterService>.Instance, locService);

        // Missing printer
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            printerService.PrintTestReceiptAsync(null, "A4", false, "gu-IN"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            printerService.PrintTestReceiptAsync("   ", "A4", false, "gu-IN"));

        // Non-existent printer
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            printerService.PrintTestReceiptAsync("CompletelyFakeNonExistentPrinter_12345", "A4", false, "gu-IN"));
    }

    [Theory]
    [InlineData("en-US", "No printer available", "PRINT TEST RECEIPT", "Printing OK")]
    [InlineData("gu-IN", "કોઈ પ્રિન્ટર ઉપલબ્ધ નથી", "ટેસ્ટ રસીદ પ્રિન્ટ", "પ્રિન્ટિંગ સફળ")]
    [InlineData("hi-IN", "कोई प्रिंटर उपलब्ध नहीं है", "टेस्ट रसीद प्रिंट", "प्रिंटिंग ठीक")]
    public void LocalizationService_HasRequiredPrintingKeys(string lang, string expectedNoPrinter, string expectedTitle, string expectedStatus)
    {
        var loc = new LocalizationService();
        Assert.Equal(expectedNoPrinter, loc.GetString("NoPrinterAvailable", lang));
        Assert.Equal(expectedTitle, loc.GetString("PrintTestReceiptTitle", lang));
        Assert.Equal(expectedStatus, loc.GetString("StatusPrintingOk", lang));
    }

    private sealed class TestDatabasePathService : IDatabasePathService
    {
        public TestDatabasePathService(string dir)
        {
            ApplicationDataDirectory = dir;
            DatabaseDirectory = Path.Combine(dir, "Data");
            DatabasePath = Path.Combine(DatabaseDirectory, "dhirdhar.db");
            BackupDirectory = Path.Combine(dir, "Backups");
            LogDirectory = Path.Combine(dir, "Logs");
        }

        public string ApplicationDataDirectory { get; }
        public string DatabaseDirectory { get; }
        public string DatabasePath { get; }
        public string BackupDirectory { get; }
        public string LogDirectory { get; }
    }
}
