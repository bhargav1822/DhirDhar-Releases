using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Reports.Models;
using DhirDhar.Application.Search;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Reports;
using DhirDhar.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class IndicLanguageInputTests
{
    [Theory]
    [InlineData("ભાર્ગવ પંચાલ", "પ્રવિણચંદ્ર", "પંચાલ", "પાટણ", "અમદાવાદ સરનામું", "ગ્રાહક નોંધો")] // Gujarati
    [InlineData("भार्गव पंचाल", "प्रविणचन्द्र", "पंचाल", "पाटन", "अहमदाबाद पता", "ग्राहक टिप्पणी")] // Hindi
    [InlineData("भार्गव पांचाळ", "प्रविणचंद्र", "पांचाळ", "मुंबई", "पुणे पत्ता", "टीप")] // Marathi
    [InlineData("ভার্গব পাঞ্চাল", "প্রবীণচন্দ্র", "পাঞ্চাল", "কলকাতা", "ঠিকানা", "নোট")] // Bengali
    [InlineData("ਭਾਰਗਵ ਪੰਚਾਲ", "ਪ੍ਰਵੀਣਚੰਦਰ", "ਪੰਚਾਲ", "ਅੰਮ੍ਰਿਤਸਰ", "ਪਤਾ", "ਨੋਟ")] // Punjabi
    [InlineData("பார்கவ் பஞ்சால்", "பிரவீன்சந்திரா", "பஞ்சால்", "சென்னை", "முகவரி", "குறிப்பு")] // Tamil
    [InlineData("భార్గవ్ పంచాల్", "ప్రవీణ్చంద్ర", "పంచాల్", "హైదరాబాద్", "చిరునామా", "గమనిక")] // Telugu
    [InlineData("ಭಾರ್ಗವ್ ಪಂಚಾಲ್", "ಪ್ರವೀಣಚಂದ್ರ", "ಪಂಚಾಲ್", "ಬೆಂಗಳೂರು", "ವಿಳಾಸ", "ಟಿಪ್ಪಣಿ")] // Kannada
    [InlineData("ഭാർഗവ് പഞ്ചാൽ", "പ്രവീൺചന്ദ്ര", "പഞ്ചാൽ", "കൊച്ചി", "മേൽവിലാസം", "കുറിപ്പ്")] // Malayalam
    [InlineData("ଭାର୍ଗବ ପଞ୍ଚାଲ", "ପ୍ରବୀଣଚନ୍ଦ୍ର", "ପଞ୍ଚାଲ", "ଭୁବନେଶ୍ୱର", "ଠିକଣା", "ଟିପ୍ପଣୀ")] // Odia
    [InlineData("ভাৰ্গৱ পাঞ্চাল", "প্ৰবীণচন্দ্ৰ", "পাঞ্চাল", "গুৱাহাটী", "ঠিকনা", "টোকা")] // Assamese
    [InlineData("Bhargav Panchal - ભાર્ગવ પંચાલ", "Pravinchandra", "Panchal", "Patan", "Address - સરનામું", "Mixed notes - નોંધો")] // Mixed Language
    public async Task SQLite_Stores_And_Retrieves_Exact_Indic_Unicode_Text(
        string name, string fatherName, string surname, string village, string address, string notes)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Shared");
        connection.Open();

        var options = new DbContextOptionsBuilder<DhirDharDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var context = new DhirDharDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            var borrower = new Borrower(
                borrowerNumber: "BOR-INDIC-001",
                name: name,
                fatherName: fatherName,
                surname: surname,
                village: village,
                phone: "9876543210",
                address: address,
                notes: notes,
                aadharNumber: "123456789012");

            context.Borrowers.Add(borrower);
            await context.SaveChangesAsync();
        }

        using (var context = new DhirDharDbContext(options))
        {
            var fetched = await context.Borrowers.FirstOrDefaultAsync(b => b.BorrowerNumber == "BOR-INDIC-001");
            Assert.NotNull(fetched);
            Assert.Equal(name, fetched.Name);
            Assert.Equal(fatherName, fetched.FatherName);
            Assert.Equal(surname, fetched.Surname);
            Assert.Equal(village, fetched.Village);
            Assert.Equal(address, fetched.Address);
            Assert.Equal(notes, fetched.Notes);
        }
    }

    [Fact]
    public async Task SearchService_Finds_Exact_Indic_Unicode_And_Mixed_Text()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Shared");
        connection.Open();

        var options = new DbContextOptionsBuilder<DhirDharDbContext>()
            .UseSqlite(connection)
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddDbContext<DhirDharDbContext>(opt => opt.UseSqlite(connection));
        services.AddLogging();
        services.AddScoped<ISearchService, SearchService>();
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Borrowers.Add(new Borrower("B1", "ભાર્ગવ પંચાલ", "પ્રવિણચંદ્ર", "પંચાલ", "પાટણ", "9876543210", "Address", "Notes", "123456789012"));
            db.Borrowers.Add(new Borrower("B2", "भार्गव पंचाल", "प्रविणचन्द्र", "पंचाल", "पाटन", "9876543211", "Address", "Notes", "123456789013"));
            db.Borrowers.Add(new Borrower("B3", "Bhargav Panchal - ભાર્ગવ પંચાલ", "Pravinchandra", "Panchal", "Patan", "9876543212", "Address", "Notes", "123456789014"));
            await db.SaveChangesAsync();
        }

        var searchService = provider.GetRequiredService<ISearchService>();

        // 1. Search Gujarati Unicode query
        var guResult = await searchService.SearchBorrowersAsync("ભાર્ગવ", "All", null, null);
        Assert.True(guResult.Count >= 2);
        Assert.Contains(guResult, b => b.Name.Contains("ભાર્ગવ"));

        // 2. Search Hindi Unicode query
        var hiResult = await searchService.SearchBorrowersAsync("भार्गव", "All", null, null);
        Assert.True(hiResult.Count >= 1);
        Assert.Contains(hiResult, b => b.Name.Contains("भार्गव"));

        // 3. Search Latin query
        var enResult = await searchService.SearchBorrowersAsync("Bhargav", "All", null, null);
        Assert.True(enResult.Count >= 1);
        Assert.Contains(enResult, b => b.Name.Contains("Bhargav"));
    }

    [Fact]
    public async Task SearchService_Searching_Palak_Or_GujaratiPalak_Finds_Borrower()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Shared");
        connection.Open();

        var options = new DbContextOptionsBuilder<DhirDharDbContext>()
            .UseSqlite(connection)
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddDbContext<DhirDharDbContext>(opt => opt.UseSqlite(connection));
        services.AddLogging();
        services.AddScoped<ISearchService, SearchService>();
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Borrowers.Add(new Borrower("B-PALAK", "પલક પટેલ", "મનન", "પટેલ", "અમદાવાદ", "9876543210", "Address", "Notes", "123456789015"));
            await db.SaveChangesAsync();
        }

        var searchService = provider.GetRequiredService<ISearchService>();

        // Searching "Palak" (Latin) transliterates to "પલક" and finds the borrower
        var latinSearch = await searchService.SearchBorrowersAsync("Palak", "All", null, null);
        Assert.Single(latinSearch);
        Assert.Equal("પલક પટેલ", latinSearch[0].Name);

        // Searching "પલક" (Gujarati Unicode) finds the borrower
        var gujaratiSearch = await searchService.SearchBorrowersAsync("પલક", "All", null, null);
        Assert.Single(gujaratiSearch);
        Assert.Equal("પલક પટેલ", gujaratiSearch[0].Name);
    }

    [Fact]
    public void IndicFontResolver_Resolves_Font_Data_For_PdfSharpCore()
    {
        var resolver = new IndicFontResolver();
        var info = resolver.ResolveTypeface("Arial", isBold: false, isItalic: false);
        Assert.NotNull(info);

        var fontBytes = resolver.GetFont(info.FaceName);
        Assert.NotNull(fontBytes);
        Assert.True(fontBytes.Length > 0);
    }

    [Fact]
    public async Task PdfExportService_Generates_Pdf_With_Indic_Unicode_Text()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"DhirDharTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);

        var mockPathService = new TestDatabasePathService(tempPath);
        var exportService = new PdfExportService(mockPathService, NullLogger<PdfExportService>.Instance);

        var items = new[]
        {
            new BorrowerSummaryItem("BOR-001", "ભાર્ગવ પંચાલ - Gujarati", "9876543210", 50000m, 10000m, 1500m, 41500m, 41500m, "Active", DateTime.Now),
            new BorrowerSummaryItem("BOR-002", "भार्गव पंचाल - Hindi", "9876543211", 20000m, 5000m, 600m, 15600m, 15600m, "Active", DateTime.Now)
        };

        var report = new BorrowerSummaryReport(
            DateTime.Now,
            2,
            2,
            0,
            0,
            items,
            15000m,
            70000m,
            2100m,
            57100m);

        var pdfPath = await exportService.ExportReportToPdfAsync(report, "BorrowerSummary");

        Assert.True(File.Exists(pdfPath));
        var fileInfo = new FileInfo(pdfPath);
        Assert.True(fileInfo.Length > 1000);

        try { Directory.Delete(tempPath, true); } catch { }
    }

    [Fact]
    public void ScriptTranslator_Phonetic_And_Native_Indic_Transliteration_Works()
    {
        // 1. Latin phonetic inputs transliterate to Gujarati Unicode
        var bhargav = ScriptTranslator.Translate("bhargav", "gu-IN");
        Assert.True(bhargav == "ભારગવ" || bhargav == "ભાર્ગવ");

        var panchal = ScriptTranslator.Translate("panchal", "gu-IN");
        Assert.Equal("પંચાલ", panchal);

        var sukhsar = ScriptTranslator.Translate("sukhsar", "gu-IN");
        Assert.Equal("સુખસર", sukhsar);

        // 2. Native Gujarati Unicode input is preserved exact
        var nativeGujarati = ScriptTranslator.Translate("ભાર્ગવ પંચાલ સુખસર", "gu-IN");
        Assert.Equal("ભાર્ગવ પંચાલ સુખસર", nativeGujarati);

        // 3. Native Hindi Unicode input is preserved exact
        var nativeHindi = ScriptTranslator.Translate("भार्गव पंचाल सुखसर", "hi-IN");
        Assert.Equal("भार्गव पंचाल सुखसर", nativeHindi);

        // 4. ScriptTranslator.IsIndicScript identifies Indian script characters
        Assert.True(ScriptTranslator.IsIndicScript("ભાર્ગવ"));
        Assert.True(ScriptTranslator.IsIndicScript("भार्गव"));
        Assert.False(ScriptTranslator.IsIndicScript("Bhargav"));
    }

    private sealed class TestDatabasePathService : DhirDhar.Application.Abstractions.Persistence.IDatabasePathService
    {
        public TestDatabasePathService(string root) => ApplicationDataDirectory = root;
        public string ApplicationDataDirectory { get; }
        public string DatabaseDirectory => Path.Combine(ApplicationDataDirectory, "Data");
        public string DatabasePath => Path.Combine(DatabaseDirectory, "DhirDhar.db");
        public string DatabaseFilePath => DatabasePath;
        public string BackupDirectory => Path.Combine(ApplicationDataDirectory, "Backups");
        public string LogDirectory => Path.Combine(ApplicationDataDirectory, "Logs");
        public string LogsDirectory => LogDirectory;
        public void EnsureDirectoriesExist() => Directory.CreateDirectory(ApplicationDataDirectory);
    }
}
