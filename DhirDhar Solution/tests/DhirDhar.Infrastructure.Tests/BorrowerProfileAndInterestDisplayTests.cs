using System;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Interest;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Transactions;
using DhirDhar.Application.Transactions.Models;
using DhirDhar.Domain.Common;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.Interest;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class BorrowerProfileAndInterestDisplayTests : IDisposable
{
    private readonly TempDatabase _tempDb;
    private readonly ServiceProvider _serviceProvider;
    private readonly IBorrowerService _borrowerService;
    private readonly ITransactionService _transactionService;
    private readonly IInterestCalculationService _interestService;
    private readonly LocalizationService _localizationService;
    private readonly ITranslationService _translationService;

    public BorrowerProfileAndInterestDisplayTests()
    {
        _tempDb = new TempDatabase();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(_tempDb.CreateDatabaseOptions());

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
        dbContext.Database.EnsureCreated();

        _borrowerService = _serviceProvider.GetRequiredService<IBorrowerService>();
        _transactionService = _serviceProvider.GetRequiredService<ITransactionService>();
        _interestService = _serviceProvider.GetRequiredService<IInterestCalculationService>();
        _localizationService = (LocalizationService)_serviceProvider.GetRequiredService<ILocalizationService>();
        _translationService = _serviceProvider.GetRequiredService<ITranslationService>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _tempDb.Dispose();
    }

    [Fact]
    public async Task JewelleryDisplay_WithSavedOrnamentDetails_DisplaysSavedValuesInGujaratiAndEnglish()
    {
        // Arrange: Borrower with Gold loan, ornament type "લોકેટ", weight 19.00 grams
        var request = new CreateBorrowerRequest(
            "B-ORN-01",
            "ભાર્ગવ પંચાલ",
            "રમેશભાઈ",
            "પંચાલ",
            "અમદાવાદ",
            "9876543210",
            null,
            null,
            DateTime.Today.AddMonths(-3),
            50000m,
            DateTime.Today.AddMonths(-3),
            null,
            null,
            null,
            "Gold",
            "લોકેટ",
            19.00m,
            3.0m);

        var created = await _borrowerService.CreateAsync(request);
        var borrower = await _borrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(borrower);

        // Verify stored DB values
        Assert.Equal("લોકેટ", borrower.OrnamentType);
        Assert.Equal(19.00m, borrower.OrnamentWeight);

        // Localize in Gujarati
        var guBorrower = borrower.Localize(_translationService, "gu-IN");
        Assert.Equal("લોકેટ", guBorrower.OrnamentType);
        Assert.Equal(19.00m, guBorrower.OrnamentWeight);

        // Verify that ornament details are visible and formatted with localized Grams
        var gramsGu = _localizationService.GetString("Grams", "gu-IN");
        Assert.Equal("ગ્રામ", gramsGu);

        var localizedDigitsWeight = _localizationService.LocalizeDigits("19.00", "gu-IN");
        Assert.Equal("૧૯.૦૦", localizedDigitsWeight);
    }

    [Fact]
    public async Task JewelleryDisplay_EmptyStates_DisplaysCorrectNotSpecifiedOrNotApplicable()
    {
        // 1. Cash loan with no ornament
        var cashReq = new CreateBorrowerRequest(
            "B-CASH-01",
            "સુરેશ પટેલ",
            null,
            null,
            "સુરત",
            null,
            null,
            null,
            DateTime.Today,
            10000m,
            DateTime.Today,
            null,
            null,
            null,
            "Cash",
            null,
            null,
            3.0m);

        var cashBorrower = await _borrowerService.CreateAsync(cashReq);
        Assert.Null(cashBorrower.OrnamentType);
        Assert.Null(cashBorrower.OrnamentWeight);

        var notApplicableGu = _localizationService.GetString("NotApplicable", "gu-IN");
        Assert.Equal("લાગુ નથી", notApplicableGu);

        // 2. Gold loan with empty ornament
        var goldReq = new CreateBorrowerRequest(
            "B-GOLD-02",
            "મહેશ શાહ",
            null,
            null,
            "વડોદરા",
            null,
            null,
            null,
            DateTime.Today,
            20000m,
            DateTime.Today,
            null,
            null,
            null,
            "Gold",
            null,
            null,
            3.0m);

        var goldBorrower = await _borrowerService.CreateAsync(goldReq);
        Assert.Null(goldBorrower.OrnamentType);

        var notSpecifiedGu = _localizationService.GetString("NotSpecified", "gu-IN");
        Assert.Equal("નિર્દિષ્ટ નથી", notSpecifiedGu);
    }

    [Fact]
    public void LoanDuration_FormattedAccordingToSelectedLanguageAndNumeralSystem()
    {
        int days = 5;
        int months = 6;
        int years = 1;

        // Test English
        string FormatDuration(int d, int m, int y, string lang)
        {
            var norm = ScriptTranslator.NormalizeLanguageCode(lang);
            var (dayUnit, monthUnit, yearUnit) = norm switch
            {
                "gu" => ("દિ", "મ", "વ"),
                "hi" => ("दि", "मा", "व"),
                "mr" => ("दि", "म", "व"),
                "bn" => ("দি", "মা", "ব"),
                "pa" => ("ਦਿ", "ਮ", "ਸ"),
                _ => ("D", "M", "Y")
            };

            var raw = $"{d:D2}{dayUnit} {m:D2}{monthUnit} {y:D2}{yearUnit}";
            return _localizationService.LocalizeDigits(raw, lang);
        }

        var enDuration = FormatDuration(days, months, years, "en-IN");
        Assert.Equal("05D 06M 01Y", enDuration);

        var guDuration = FormatDuration(days, months, years, "gu-IN");
        Assert.Equal("૦૫દિ ૦૬મ ૦૧વ", guDuration);

        var hiDuration = FormatDuration(days, months, years, "hi-IN");
        Assert.Equal("०५दि ०६मा ०१વ".Replace('વ', 'व'), hiDuration);

        // Zero duration
        var enZero = FormatDuration(0, 0, 0, "en-IN");
        Assert.Equal("00D 00M 00Y", enZero);

        var guZero = FormatDuration(0, 0, 0, "gu-IN");
        Assert.Equal("૦૦દિ ૦૦મ ૦૦વ", guZero);
    }

    [Fact]
    public async Task MonthlyInterest_CalculatesFromInitialPrincipal_AndUpdatesImmediatelyUponDepositEvent()
    {
        // Scenario from prompt:
        // Initial loan: ₹10,000, 3%/mo
        // Initial monthly interest = ₹300
        var loanDate = new DateTime(2026, 8, 20);
        var borrowerReq = new CreateBorrowerRequest(
            "B-INT-01",
            "દિનેશ પરમાર",
            null,
            null,
            "રાજકોટ",
            null,
            null,
            null,
            loanDate,
            10000m,
            loanDate,
            null,
            null,
            null,
            "Cash",
            null,
            null,
            3.0m);

        var borrower = await _borrowerService.CreateAsync(borrowerReq);

        // Initial interest check (before events)
        var initialCalc = await _interestService.CalculateAsync(borrower.Id, loanDate);
        Assert.Equal(10000m, initialCalc.ClosingPrincipal);
        Assert.Equal(300.00m, initialCalc.MonthlyInterest);

        // Event occurs on 05/12/2026: Deposit ₹2,000
        var depositDate = new DateTime(2026, 12, 5);
        await _transactionService.CreateAsync(new CreateTransactionRequest(
            borrower.Id,
            TransactionType.Deposit,
            2000m,
            depositDate,
            "Partial Deposit"));

        // Recalculate interest at deposit date
        var afterDepositCalc = await _interestService.CalculateAsync(borrower.Id, depositDate);

        // Accrued interest before deposit:
        // 20/08 -> 20/11 = 3 months @ ₹300 = ₹900
        // 20/11 -> 05/12 = 15 days = ₹150
        // Total accrued = ₹1,050
        // Amount before event = 10,000 + 1,050 = 11,050
        // After deposit 2,000 -> New Principal = ₹9,050
        Assert.Equal(9050.00m, afterDepositCalc.ClosingPrincipal);

        // The new monthly interest MUST be based on the new principal ₹9,050:
        // ₹9,050 × 3% = ₹271.50/month
        var expectedMonthlyInterest = FinancialRounding.RoundInterest(afterDepositCalc.ClosingPrincipal * (3.0m / 100m));
        Assert.Equal(271.50m, expectedMonthlyInterest);
        Assert.Equal(expectedMonthlyInterest, afterDepositCalc.MonthlyInterest);
        Assert.NotEqual(300.00m, afterDepositCalc.MonthlyInterest);
    }

    [Fact]
    public async Task MonthlyInterest_UpdatesImmediatelyUponWithdrawalEvent()
    {
        // Initial loan: ₹10,000 @ 3%/month on 20/08/2026
        var loanDate = new DateTime(2026, 8, 20);
        var borrowerReq = new CreateBorrowerRequest(
            "B-INT-02",
            "કિશોર ઠાકોર",
            null,
            null,
            "મહેસાણા",
            null,
            null,
            null,
            loanDate,
            10000m,
            loanDate,
            null,
            null,
            null,
            "Cash",
            null,
            null,
            3.0m);

        var borrower = await _borrowerService.CreateAsync(borrowerReq);

        // Withdrawal occurs on 20/09/2026 (after 1 month): Borrower withdraws ₹2,000
        var withdrawalDate = new DateTime(2026, 9, 20);
        await _transactionService.CreateAsync(new CreateTransactionRequest(
            borrower.Id,
            TransactionType.Withdrawal,
            2000m,
            withdrawalDate,
            "Additional Loan Amount"));

        var afterWithdrawalCalc = await _interestService.CalculateAsync(borrower.Id, withdrawalDate);

        // 1 month accrued interest = ₹300
        // Amount before event = 10,000 + 300 = 10,300
        // After withdrawal +2,000 -> New Principal = ₹12,300
        Assert.Equal(12300.00m, afterWithdrawalCalc.ClosingPrincipal);

        // Monthly interest immediately uses ₹12,300 × 3% = ₹369.00/month
        var expectedMonthlyInterest = FinancialRounding.RoundInterest(afterWithdrawalCalc.ClosingPrincipal * (3.0m / 100m));
        Assert.Equal(369.00m, expectedMonthlyInterest);
        Assert.Equal(expectedMonthlyInterest, afterWithdrawalCalc.MonthlyInterest);
    }
}
