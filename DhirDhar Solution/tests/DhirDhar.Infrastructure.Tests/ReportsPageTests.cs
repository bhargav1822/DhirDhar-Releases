using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Reports;
using DhirDhar.Application.Reports.Models;
using DhirDhar.Application.Transactions;
using DhirDhar.Application.Transactions.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class ReportsPageTests : IDisposable
{
    private readonly TempDatabase _tempDb;
    private readonly ServiceProvider _serviceProvider;
    private readonly IReportService _reportService;
    private readonly IPdfExportService _pdfExportService;
    private readonly IBorrowerService _borrowerService;
    private readonly ITransactionService _transactionService;
    private readonly ILocalizationService _localizationService;

    public ReportsPageTests()
    {
        _tempDb = new TempDatabase();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(_tempDb.CreateDatabaseOptions());

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
        dbContext.Database.EnsureCreated();

        _reportService = _serviceProvider.GetRequiredService<IReportService>();
        _pdfExportService = _serviceProvider.GetRequiredService<IPdfExportService>();
        _borrowerService = _serviceProvider.GetRequiredService<IBorrowerService>();
        _transactionService = _serviceProvider.GetRequiredService<ITransactionService>();
        _localizationService = _serviceProvider.GetRequiredService<ILocalizationService>();
    }

    [Fact]
    public async Task GenerateBorrowerStatementAsync_CalculatesCorrectFinancialData()
    {
        _localizationService.SetLanguage("en-IN");
        // Arrange
        var loanDate = new DateTime(2026, 1, 1);
        var borrower = await _borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "B-101",
            Name: "Ramesh Patel",
            FatherName: "Kantilal",
            Surname: "Patel",
            Village: "Ahmedabad",
            Contact: "9876543210",
            Address: "Ahmedabad",
            AadharNumber: "123456789012",
            EntryDate: loanDate,
            LoanAmount: 100000m,
            LoanDate: loanDate,
            Notes: null,
            BorrowerPhotoPath: null,
            OrnamentPhotoPath: null,
            LoanType: "Cash",
            OrnamentType: null,
            OrnamentWeight: null,
            InterestRate: 2.5m
        ));

        // Add additional withdrawal (Give Amount)
        await _transactionService.CreateAsync(new CreateTransactionRequest(
            BorrowerId: borrower.Id,
            Type: TransactionType.Withdrawal,
            Amount: 20000m,
            TransactionDate: new DateTime(2026, 2, 1),
            Description: "Additional Loan"
        ));

        // Add deposit (Receive Payment)
        await _transactionService.CreateAsync(new CreateTransactionRequest(
            BorrowerId: borrower.Id,
            Type: TransactionType.Deposit,
            Amount: 30000m,
            TransactionDate: new DateTime(2026, 3, 1),
            Description: "Partial Repayment"
        ));

        // Act - Generate Borrower Statement for Jan to April 2026
        var fromDate = new DateTime(2026, 1, 1);
        var toDate = new DateTime(2026, 4, 1);
        var statement = await _reportService.GenerateBorrowerStatementAsync(borrower.Id, fromDate, toDate);

        // Assert
        Assert.NotNull(statement);
        Assert.Equal(borrower.BorrowerNumber, statement.BorrowerNumber);
        Assert.Equal("Ramesh Patel", statement.BorrowerName);
        Assert.Equal("9876543210", statement.Contact);
        Assert.Equal(2.5m, statement.InterestRate);
        Assert.True(statement.OpeningPrincipal > 0);
        Assert.True(statement.TotalDeposits >= 30000m);
        Assert.True(statement.TotalWithdrawals >= 20000m);
        Assert.True(statement.FinalOutstanding > 0);
        Assert.NotEmpty(statement.FinancialHistory);

        // Verify helper properties
        Assert.Equal("₹ 30,000.00", statement.FormattedTotalDeposits);
        Assert.False(string.IsNullOrWhiteSpace(statement.FormattedInterestRate));
        Assert.False(string.IsNullOrWhiteSpace(statement.FormattedDateRange));
        Assert.True(statement.HasFinancialHistory);
    }

    [Fact]
    public async Task GenerateBorrowerStatementAsync_ValidatesDateOrder()
    {
        var borrower = await _borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "B-102",
            Name: "Test User",
            FatherName: "Father",
            Surname: "Surname",
            Village: "Village",
            Contact: "9876543212",
            Address: "Address",
            AadharNumber: "123456789012",
            EntryDate: DateTime.Today,
            LoanAmount: 50000m,
            LoanDate: DateTime.Today,
            Notes: null,
            BorrowerPhotoPath: null,
            OrnamentPhotoPath: null,
            LoanType: "Cash",
            OrnamentType: null,
            OrnamentWeight: null,
            InterestRate: 3.0m
        ));

        var fromDate = DateTime.Today;
        var toDate = DateTime.Today.AddDays(-10);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _reportService.GenerateBorrowerStatementAsync(borrower.Id, fromDate, toDate));
    }

    [Fact]
    public async Task ExportReportToPdfAsync_GeneratesValidPdfFile()
    {
        var borrower = await _borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "B-103",
            Name: "Bhargav Panchal",
            FatherName: "Pravin",
            Surname: "Panchal",
            Village: "Sukhsar",
            Contact: "9988776655",
            Address: "Sukhsar",
            AadharNumber: "123456789012",
            EntryDate: new DateTime(2026, 1, 1),
            LoanAmount: 75000m,
            LoanDate: new DateTime(2026, 1, 1),
            Notes: null,
            BorrowerPhotoPath: null,
            OrnamentPhotoPath: null,
            LoanType: "Gold/Silver",
            OrnamentType: "Ring",
            OrnamentWeight: 15.5m,
            InterestRate: 3.0m
        ));

        var statement = await _reportService.GenerateBorrowerStatementAsync(
            borrower.Id,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 3, 31));

        var pdfPath = await _pdfExportService.ExportReportToPdfAsync(statement, "BorrowerStatement");

        Assert.NotNull(pdfPath);
        Assert.True(File.Exists(pdfPath));
        var fileInfo = new FileInfo(pdfPath);
        Assert.True(fileInfo.Length > 0);

        // Cleanup
        try { File.Delete(pdfPath); } catch { }
    }

    [Fact]
    public async Task GenerateTransactionReportAsync_And_OtherReports_ExecuteSuccessfully()
    {
        var fromDate = new DateTime(2026, 1, 1);
        var toDate = new DateTime(2026, 12, 31);

        var txnReport = await _reportService.GenerateTransactionReportAsync(fromDate, toDate, null, "All");
        Assert.NotNull(txnReport);

        var interestReport = await _reportService.GenerateInterestReportAsync(null, fromDate, toDate);
        Assert.NotNull(interestReport);

        var outstandingReport = await _reportService.GenerateOutstandingReportAsync(null);
        Assert.NotNull(outstandingReport);

        var summaryReport = await _reportService.GenerateBorrowerSummaryAsync(null);
        Assert.NotNull(summaryReport);
    }

    [Fact]
    public async Task BorrowerDateResolution_FetchesActualStoredDate_ForMultipleBorrowers()
    {
        // Arrange: Create 3 borrowers with distinct registration and loan dates
        var date1 = new DateTime(2021, 6, 15);
        var date2 = new DateTime(2023, 11, 20);
        var date3 = new DateTime(2025, 3, 5);

        var borrower1 = await _borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "B-201",
            Name: "Amratbhai Parmar",
            FatherName: "Valjibhai",
            Surname: "Parmar",
            Village: "Gandhinagar",
            Contact: "9876543201",
            Address: "Sector 21",
            AadharNumber: "111122223333",
            EntryDate: date1,
            LoanAmount: 50000m,
            LoanDate: date1,
            Notes: null,
            BorrowerPhotoPath: null,
            OrnamentPhotoPath: null,
            LoanType: "Gold",
            OrnamentType: "Chain",
            OrnamentWeight: 15.5m,
            InterestRate: 2.0m
        ));

        var borrower2 = await _borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "B-202",
            Name: "Bhavesh Dave",
            FatherName: "Hasmukhbhai",
            Surname: "Dave",
            Village: "Rajkot",
            Contact: "9876543202",
            Address: "Kalawad Road",
            AadharNumber: "222233334444",
            EntryDate: date2,
            LoanAmount: 75000m,
            LoanDate: date2,
            Notes: null,
            BorrowerPhotoPath: null,
            OrnamentPhotoPath: null,
            LoanType: "Silver",
            OrnamentType: "Kandora",
            OrnamentWeight: 250m,
            InterestRate: 3.0m
        ));

        var borrower3 = await _borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "B-203",
            Name: "Chandresh Solanki",
            FatherName: "Mohanbhai",
            Surname: "Solanki",
            Village: "Bhavnagar",
            Contact: "9876543203",
            Address: "Chitra",
            AadharNumber: "333344445555",
            EntryDate: date3,
            LoanAmount: 120000m,
            LoanDate: date3,
            Notes: null,
            BorrowerPhotoPath: null,
            OrnamentPhotoPath: null,
            LoanType: "Cash",
            OrnamentType: null,
            OrnamentWeight: null,
            InterestRate: 2.5m
        ));

        // Act & Assert 1: Query Borrower 1
        var dbBorrower1 = await _borrowerService.GetByIdAsync(borrower1.Id);
        Assert.NotNull(dbBorrower1);
        var resolvedDate1 = (dbBorrower1.LoanDate.HasValue && dbBorrower1.LoanDate.Value != default)
            ? (dbBorrower1.EntryDate != default && dbBorrower1.EntryDate < dbBorrower1.LoanDate.Value ? dbBorrower1.EntryDate : dbBorrower1.LoanDate.Value)
            : dbBorrower1.EntryDate;
        Assert.Equal(date1.Date, resolvedDate1.Date);

        // Act & Assert 2: Query Borrower 2
        var dbBorrower2 = await _borrowerService.GetByIdAsync(borrower2.Id);
        Assert.NotNull(dbBorrower2);
        var resolvedDate2 = (dbBorrower2.LoanDate.HasValue && dbBorrower2.LoanDate.Value != default)
            ? (dbBorrower2.EntryDate != default && dbBorrower2.EntryDate < dbBorrower2.LoanDate.Value ? dbBorrower2.EntryDate : dbBorrower2.LoanDate.Value)
            : dbBorrower2.EntryDate;
        Assert.Equal(date2.Date, resolvedDate2.Date);

        // Act & Assert 3: Query Borrower 3
        var dbBorrower3 = await _borrowerService.GetByIdAsync(borrower3.Id);
        Assert.NotNull(dbBorrower3);
        var resolvedDate3 = (dbBorrower3.LoanDate.HasValue && dbBorrower3.LoanDate.Value != default)
            ? (dbBorrower3.EntryDate != default && dbBorrower3.EntryDate < dbBorrower3.LoanDate.Value ? dbBorrower3.EntryDate : dbBorrower3.LoanDate.Value)
            : dbBorrower3.EntryDate;
        Assert.Equal(date3.Date, resolvedDate3.Date);

        // Verify dates are distinctly different and not hardcoded defaults
        Assert.NotEqual(resolvedDate1.Date, resolvedDate2.Date);
        Assert.NotEqual(resolvedDate2.Date, resolvedDate3.Date);
        Assert.NotEqual(new DateTime(2021, 4, 1), resolvedDate1.Date);
    }

    [Fact]
    public async Task BorrowerStatementReport_UsesCorrectBorrowerStartDate_InCalculationsAndPdf()
    {
        // Arrange
        var customBorrowerDate = new DateTime(2022, 9, 10);
        var toDate = DateTime.Today;

        var borrower = await _borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "B-204",
            Name: "Dharmesh Vaghela",
            FatherName: "Jayeshbhai",
            Surname: "Vaghela",
            Village: "Surat",
            Contact: "9876543204",
            Address: "Varachha",
            AadharNumber: "444455556666",
            EntryDate: customBorrowerDate,
            LoanAmount: 60000m,
            LoanDate: customBorrowerDate,
            Notes: null,
            BorrowerPhotoPath: null,
            OrnamentPhotoPath: null,
            LoanType: "Gold",
            OrnamentType: "Ring",
            OrnamentWeight: 10m,
            InterestRate: 2.0m
        ));

        // Act: Generate statement using borrower's actual date
        var statement = await _reportService.GenerateBorrowerStatementAsync(borrower.Id, customBorrowerDate, toDate);

        // Assert
        Assert.NotNull(statement);
        Assert.Equal(customBorrowerDate.Date, statement.FromDate.Date);
        Assert.Equal(toDate.Date, statement.ToDate.Date);
        Assert.Equal(customBorrowerDate.Date, statement.EntryDate.Date);

        // Export PDF and verify
        var pdfPath = await _pdfExportService.ExportReportToPdfAsync(statement, "BorrowerStatement");
        Assert.NotNull(pdfPath);
        Assert.True(File.Exists(pdfPath));
        Assert.True(new FileInfo(pdfPath).Length > 0);

        try { File.Delete(pdfPath); } catch { }
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        _tempDb?.Dispose();
    }
}
