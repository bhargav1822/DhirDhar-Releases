using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Interest;
using DhirDhar.Application.Ledger;
using DhirDhar.Application.Search;
using DhirDhar.Application.Search.Models;
using DhirDhar.Application.Transactions;
using DhirDhar.Application.Transactions.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Audit;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Caching;
using DhirDhar.Infrastructure.Interest;
using DhirDhar.Infrastructure.Ledger;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Search;
using DhirDhar.Infrastructure.Tests.Persistence;
using DhirDhar.Infrastructure.Transactions;
using DhirDhar.Infrastructure.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class ComprehensiveSearchTests : IDisposable
{
    private readonly TempDatabase _tempDb;
    private readonly ServiceProvider _serviceProvider;
    private readonly IBorrowerService _borrowerService;
    private readonly ITransactionService _transactionService;
    private readonly ISearchService _searchService;
    private readonly ILedgerService _ledgerService;
    private readonly DbContextOptions<DhirDharDbContext> _options;

    public ComprehensiveSearchTests()
    {
        _tempDb = new TempDatabase();
        _options = _tempDb.CreateOptions();

        using (var initContext = new DhirDharDbContext(_options))
        {
            initContext.Database.EnsureCreated();
        }

        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        var cacheService = new MemoryCacheService(memoryCache, NullLogger<MemoryCacheService>.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DhirDhar.Application.Caching.ICacheService>(cacheService);
        services.AddSingleton<DhirDhar.Application.Localization.ILocalizationService, LocalizationService>();
        services.AddScoped(_ => new DhirDharDbContext(_options));
        services.AddScoped<DhirDhar.Application.Audit.IAuditService, AuditService>();
        services.AddScoped<DhirDhar.Application.Validation.IFinancialValidationService, FinancialValidationService>();
        services.AddScoped<DhirDhar.Application.Validation.IIdempotencyService, IdempotencyService>();
        services.AddScoped<IInterestCalculationService, InterestCalculationService>();
        services.AddScoped<IBorrowerService, BorrowerService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<ILedgerService, LedgerService>();
        _serviceProvider = services.BuildServiceProvider();

        _borrowerService = _serviceProvider.GetRequiredService<IBorrowerService>();
        _transactionService = _serviceProvider.GetRequiredService<ITransactionService>();
        _searchService = _serviceProvider.GetRequiredService<ISearchService>();
        _ledgerService = _serviceProvider.GetRequiredService<ILedgerService>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _tempDb.Dispose();
    }

    private async Task<Borrower> SeedBorrowerAsync(
        string borrowerNumber,
        string name,
        string fatherName,
        string surname,
        string village,
        string phone,
        string aadharNumber,
        string address = "Sample Address",
        string notes = "Sample Notes")
    {
        using var context = new DhirDharDbContext(_options);
        var borrower = new Borrower(
            borrowerNumber,
            name,
            fatherName,
            surname,
            village,
            phone,
            address,
            notes,
            aadharNumber,
            DateTime.Today.AddMonths(-6));

        context.Borrowers.Add(borrower);
        await context.SaveChangesAsync();
        return borrower;
    }

    private async Task<Transaction> SeedTransactionAsync(
        Guid borrowerId,
        TransactionType type,
        decimal amount,
        string? description,
        string? reference)
    {
        using var context = new DhirDharDbContext(_options);
        var period = await context.FinancialPeriods.FirstOrDefaultAsync();
        if (period == null)
        {
            period = new FinancialPeriod("Default Period", DateTime.Today.AddMonths(-12), DateTime.Today.AddMonths(12));
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();
        }

        var txn = new Transaction(
            borrowerId,
            period.Id,
            Money.Create(amount),
            type,
            DateTime.Today.AddDays(-10),
            description,
            reference);

        context.Transactions.Add(txn);
        await context.SaveChangesAsync();
        return txn;
    }

    [Fact]
    public async Task BorrowerSearch_EmptyQuery_RestoresFullUnfilteredList()
    {
        await SeedBorrowerAsync("DJ001", "Ramesh", "Kanjibhai", "Patel", "Surat", "9876543210", "123456789012");
        await SeedBorrowerAsync("DJ002", "Suresh", "Manibhai", "Shah", "Navsari", "9876543211", "123456789013");

        var result = await _borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 100);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);

        var emptySearch = await _borrowerService.GetListAsync(BorrowerFilter.All, "   ", 1, 100);
        Assert.Equal(2, emptySearch.TotalCount);
    }

    [Fact]
    public async Task BorrowerSearch_SupportsAllBorrowerIdentificationFields()
    {
        var b = await SeedBorrowerAsync("DJ100", "Gopal", "Dhirubhai", "Gondaliya", "Amreli", "9988776655", "889977665544", "Plot 42, Green Park", "Special VIP Client");

        // 1. Borrower Number
        var byNumber = await _borrowerService.GetListAsync(BorrowerFilter.All, "DJ100", 1, 10);
        Assert.Single(byNumber.Items);
        Assert.Equal(b.Id, byNumber.Items.First().Id);

        // 2. Name
        var byName = await _borrowerService.GetListAsync(BorrowerFilter.All, "gopal", 1, 10);
        Assert.Single(byName.Items);

        // 3. Father Name
        var byFather = await _borrowerService.GetListAsync(BorrowerFilter.All, "Dhirubhai", 1, 10);
        Assert.Single(byFather.Items);

        // 4. Surname
        var bySurname = await _borrowerService.GetListAsync(BorrowerFilter.All, "gondaliya", 1, 10);
        Assert.Single(bySurname.Items);

        // 5. Village
        var byVillage = await _borrowerService.GetListAsync(BorrowerFilter.All, "amreli", 1, 10);
        Assert.Single(byVillage.Items);

        // 6. Mobile/Phone
        var byPhone = await _borrowerService.GetListAsync(BorrowerFilter.All, "9988776655", 1, 10);
        Assert.Single(byPhone.Items);

        // 7. Aadhaar
        var byAadhar = await _borrowerService.GetListAsync(BorrowerFilter.All, "889977665544", 1, 10);
        Assert.Single(byAadhar.Items);

        // 8. Address
        var byAddress = await _borrowerService.GetListAsync(BorrowerFilter.All, "Green Park", 1, 10);
        Assert.Single(byAddress.Items);

        // 9. Notes
        var byNotes = await _borrowerService.GetListAsync(BorrowerFilter.All, "Special VIP", 1, 10);
        Assert.Single(byNotes.Items);
    }

    [Fact]
    public async Task BorrowerSearch_MultiScriptTransliteration_WorksAcrossEnglishGujaratiHindi()
    {
        // Borrower 1 stored with Gujarati name "ભાર્ગવ"
        await SeedBorrowerAsync("DJ201", "ભાર્ગવ", "કાળુભાઈ", "પટેલ", "અમદાવાદ", "9123456780", "112233445566");

        // Search with English transliterated term "Bhargav"
        var englishSearch = await _borrowerService.GetListAsync(BorrowerFilter.All, "Bhargav", 1, 10);
        Assert.Single(englishSearch.Items);
        Assert.Equal("DJ201", englishSearch.Items.First().BorrowerNumber);

        // Borrower 2 stored with English name "Panchal"
        await SeedBorrowerAsync("DJ202", "Panchal", "Kantibhai", "Desai", "Surat", "9123456781", "112233445567");

        // Search with Gujarati transliterated term "પંચાલ"
        var gujaratiSearch = await _borrowerService.GetListAsync(BorrowerFilter.All, "પંચાલ", 1, 10);
        Assert.Single(gujaratiSearch.Items);
        Assert.Equal("DJ202", gujaratiSearch.Items.First().BorrowerNumber);
    }

    [Fact]
    public async Task BorrowerSearch_GujaratiDigits_NormalizesToAscii()
    {
        await SeedBorrowerAsync("DJ500", "Ketan", "Bavchand", "Mehta", "Rajkot", "9898001122", "556677889900");

        // Search phone using Gujarati digits "૯૮૯૮૦૦૧૧૨૨"
        var gujaratiDigits = "૯૮૯૮૦૦૧૧૨૨";
        var res = await _borrowerService.GetListAsync(BorrowerFilter.All, gujaratiDigits, 1, 10);
        Assert.Single(res.Items);
        Assert.Equal("DJ500", res.Items.First().BorrowerNumber);
    }

    [Fact]
    public async Task BorrowerSearch_QrPayload_PastingRawQrExtractsAndFindsBorrower()
    {
        var b = await SeedBorrowerAsync("DJ888", "Mahesh", "Pranjivan", "Vora", "Bhavnagar", "9426001234", "998811223344");

        var qrPayload = "DHIRDHAR|ACCOUNT|DJ888";
        var res = await _borrowerService.GetListAsync(BorrowerFilter.All, qrPayload, 1, 10);
        Assert.Single(res.Items);
        Assert.Equal(b.Id, res.Items.First().Id);
    }

    [Fact]
    public async Task BorrowerSearch_MultiAccount_PreservesSeparateAccountsForSamePerson()
    {
        // Same person has two separate borrower accounts
        await SeedBorrowerAsync("DJ301", "Pravin Shah", "Manilal", "Shah", "Vadodara", "9825012345", "445566778899");
        await SeedBorrowerAsync("DJ302", "Pravin Shah", "Manilal", "Shah", "Vadodara", "9825012345", "445566778899");

        var res = await _borrowerService.GetListAsync(BorrowerFilter.All, "Pravin Shah", 1, 10);
        Assert.Equal(2, res.TotalCount);
        Assert.Contains(res.Items, item => item.BorrowerNumber == "DJ301");
        Assert.Contains(res.Items, item => item.BorrowerNumber == "DJ302");
    }

    [Fact]
    public async Task TransactionSearch_SearchesAcrossBorrowerDetailsAndTransactionFields()
    {
        var b = await SeedBorrowerAsync("DJ401", "Kishore", "Amrutlal", "Soni", "Jamnagar", "9879011223", "334455667788");
        var txn1 = await SeedTransactionAsync(b.Id, TransactionType.Deposit, 15000m, "Monthly Installment Check", "REF-TXN-9988");
        await SeedTransactionAsync(b.Id, TransactionType.Withdrawal, 50000m, "Initial Loan Given", "REF-TXN-1122");

        // 1. Search by Reference
        var byRef = await _transactionService.GetListAsync(new TransactionFilterRequest(null, TransactionTypeFilter.All, null, null, "REF-TXN-9988", 1, 10));
        Assert.Single(byRef.Items);
        Assert.Equal(txn1.Id, byRef.Items.First().Id);

        // 2. Search by Description
        var byDesc = await _transactionService.GetListAsync(new TransactionFilterRequest(null, TransactionTypeFilter.All, null, null, "Installment", 1, 10));
        Assert.Single(byDesc.Items);
        Assert.Equal(txn1.Id, byDesc.Items.First().Id);

        // 3. Search by Borrower Name
        var byBorrowerName = await _transactionService.GetListAsync(new TransactionFilterRequest(null, TransactionTypeFilter.All, null, null, "Kishore", 1, 10));
        Assert.Equal(2, byBorrowerName.Items.Count);

        // 4. Search by Borrower Number
        var byBorrowerNumber = await _transactionService.GetListAsync(new TransactionFilterRequest(null, TransactionTypeFilter.All, null, null, "DJ401", 1, 10));
        Assert.Equal(2, byBorrowerNumber.Items.Count);

        // 5. Search by Borrower Village
        var byVillage = await _transactionService.GetListAsync(new TransactionFilterRequest(null, TransactionTypeFilter.All, null, null, "Jamnagar", 1, 10));
        Assert.Equal(2, byVillage.Items.Count);
    }

    [Fact]
    public async Task GlobalSearchService_CategoryFilterIsolation_WorksCorrectly()
    {
        var b = await SeedBorrowerAsync("DJ601", "Jayesh", "Tribhovan", "Dave", "Patan", "9408012345", "778899001122");
        await SeedTransactionAsync(b.Id, TransactionType.Deposit, 20000m, "Dave Account Settlement", "SETTLE-01");

        // "All" category returns both borrower and transaction
        var allResults = await _searchService.SearchAsync(new SearchFilter(SearchTerm: "Dave", BorrowerFilter: "All"));
        Assert.Equal(2, allResults.TotalCount);
        Assert.Contains(allResults.Items, item => item.EntityType == "Borrower");
        Assert.Contains(allResults.Items, item => item.EntityType == "Transaction");

        // "Borrower" category returns only borrower
        var borrowerResults = await _searchService.SearchBorrowersAsync("Dave", null, null, null);
        Assert.Single(borrowerResults);
        Assert.Equal("DJ601", borrowerResults.First().BorrowerNumber);

        // "Transaction" category returns only transaction
        var txnResults = await _searchService.SearchTransactionsAsync("Dave", null, null, null, null, null);
        Assert.Single(txnResults);
        Assert.Equal("SETTLE-01", txnResults.First().Reference);
    }

    [Fact]
    public async Task LedgerSearch_SupportsDescriptionReferenceAndEventType()
    {
        var createReq = new CreateBorrowerRequest(
            BorrowerNumber: "DJ701",
            Name: "Hasmukh",
            FatherName: "Naranbhai",
            Surname: "Prajapati",
            Village: "Mehsana",
            Contact: "9724012345",
            Address: "Station Road",
            AadharNumber: "223344556677",
            EntryDate: DateTime.Today.AddMonths(-3),
            LoanAmount: 50000m,
            LoanDate: DateTime.Today.AddMonths(-3),
            Notes: "Special Notes",
            LoanType: "Gold",
            OrnamentType: "Chain",
            OrnamentWeight: 20m,
            InterestRate: 2m);

        var created = await _borrowerService.CreateAsync(createReq);
        await SeedTransactionAsync(created.Id, TransactionType.Deposit, 10000m, "Special Cash Payment", "REC-7788");

        // Search by event type / description
        var entries = await _ledgerService.GetEntriesAsync(created.Id, null, null, "All", "Deposit");
        Assert.NotEmpty(entries);

        // Search by reference "CR"
        var entriesByRef = await _ledgerService.GetEntriesAsync(created.Id, null, null, "All", "CR");
        Assert.NotEmpty(entriesByRef);
    }

    [Fact]
    public async Task Search_Cancellation_CancelsGracefully()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _borrowerService.GetListAsync(BorrowerFilter.All, "test", 1, 10, cts.Token);
        });
    }
}
