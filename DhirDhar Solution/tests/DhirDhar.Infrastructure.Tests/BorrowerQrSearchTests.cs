using System;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.QrCode;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.QrCode;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class BorrowerQrSearchTests : IDisposable
{
    private readonly TempDatabase _tempDb;
    private readonly DbContextOptions<DhirDharDbContext> _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BorrowerService _borrowerService;
    private readonly IQrCodeService _qrCodeService;

    public BorrowerQrSearchTests()
    {
        _tempDb = new TempDatabase();
        _options = _tempDb.CreateOptions();

        using (var initContext = new DhirDharDbContext(_options))
        {
            initContext.Database.EnsureCreated();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new DhirDharDbContext(_options));
        var sp = services.BuildServiceProvider();
        _scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        _borrowerService = new BorrowerService(_scopeFactory, NullLogger<BorrowerService>.Instance);
        _qrCodeService = new QrCodeService();
    }

    public void Dispose()
    {
        _tempDb.Dispose();
    }

    private async Task<Borrower> SeedBorrowerAsync(string name, string borrowerNumber, BorrowerStatus status = BorrowerStatus.Active)
    {
        using var context = new DhirDharDbContext(_options);
        var borrower = new Borrower(
            borrowerNumber,
            name,
            "Father",
            "Surname",
            "Village",
            "1234567890",
            "Address",
            "Notes",
            "123456789012",
            DateTime.Today.AddMonths(-3)
        );

        if (status == BorrowerStatus.Closed)
        {
            borrower.CloseAccount(DateTime.Today.AddDays(-5));
        }

        context.Borrowers.Add(borrower);
        await context.SaveChangesAsync();
        return borrower;
    }

    [Fact]
    public async Task MultiAccount_SamePersonDifferentAccounts_EachQrResolvesExactDistinctAccount()
    {
        // Ramesh Patel has 3 distinct accounts
        var acc1 = await SeedBorrowerAsync("Ramesh Patel", "DJ102");
        var acc2 = await SeedBorrowerAsync("Ramesh Patel", "DJ135");
        var acc3 = await SeedBorrowerAsync("Ramesh Patel", "DJ148");

        // QR 1 -> DHIRDHAR|ACCOUNT|DJ102
        var payload1 = _qrCodeService.FormatPayload(acc1.BorrowerNumber);
        var parsed1 = _qrCodeService.TryParsePayload(payload1, out var num1);
        Assert.True(parsed1);
        var found1 = await _borrowerService.GetByBorrowerNumberAsync(num1);
        Assert.NotNull(found1);
        Assert.Equal(acc1.Id, found1!.Id);
        Assert.Equal("DJ102", found1.BorrowerNumber);
        Assert.Contains("Ramesh Patel", found1.FullName);

        // QR 2 -> DHIRDHAR|ACCOUNT|DJ135
        var payload2 = _qrCodeService.FormatPayload(acc2.BorrowerNumber);
        var parsed2 = _qrCodeService.TryParsePayload(payload2, out var num2);
        Assert.True(parsed2);
        var found2 = await _borrowerService.GetByBorrowerNumberAsync(num2);
        Assert.NotNull(found2);
        Assert.Equal(acc2.Id, found2!.Id);
        Assert.Equal("DJ135", found2.BorrowerNumber);

        // QR 3 -> DHIRDHAR|ACCOUNT|DJ148
        var payload3 = _qrCodeService.FormatPayload(acc3.BorrowerNumber);
        var parsed3 = _qrCodeService.TryParsePayload(payload3, out var num3);
        Assert.True(parsed3);
        var found3 = await _borrowerService.GetByBorrowerNumberAsync(num3);
        Assert.NotNull(found3);
        Assert.Equal(acc3.Id, found3!.Id);
        Assert.Equal("DJ148", found3.BorrowerNumber);
    }

    [Fact]
    public async Task ClosedAccount_QrRemainsValid_ReturnsClosedAccountWithoutModifyingStatus()
    {
        var closedAcc = await SeedBorrowerAsync("Suresh Kumar", "DJ102", BorrowerStatus.Closed);

        var payload = _qrCodeService.FormatPayload("DJ102");
        var parsed = _qrCodeService.TryParsePayload(payload, out var num);
        Assert.True(parsed);

        var found = await _borrowerService.GetByBorrowerNumberAsync(num);
        Assert.NotNull(found);
        Assert.Equal(closedAcc.Id, found!.Id);
        Assert.Equal("Closed", found.Status);
    }

    [Fact]
    public async Task NonExistentAccount_QrLookupReturnsNull()
    {
        var payload = "DHIRDHAR|ACCOUNT|NONEXISTENT-999";
        var parsed = _qrCodeService.TryParsePayload(payload, out var num);
        Assert.True(parsed);

        var found = await _borrowerService.GetByBorrowerNumberAsync(num);
        Assert.Null(found);
    }

    [Fact]
    public async Task CaseInsensitiveAndPrefixHandling_ResolvesCorrectAccount()
    {
        var acc = await SeedBorrowerAsync("Kiran Shah", "DJ102");

        // Lowercase scan
        var foundLower = await _borrowerService.GetByBorrowerNumberAsync("dj102");
        Assert.NotNull(foundLower);
        Assert.Equal(acc.Id, foundLower!.Id);

        // Scan with # prefix if formatted
        var parsedHash = _qrCodeService.TryParsePayload("DHIRDHAR|ACCOUNT|#DJ102", out var cleanNum);
        Assert.True(parsedHash);
        var foundWithHash = await _borrowerService.GetByBorrowerNumberAsync(cleanNum);
        Assert.NotNull(foundWithHash);
        Assert.Equal(acc.Id, foundWithHash!.Id);
    }
}
