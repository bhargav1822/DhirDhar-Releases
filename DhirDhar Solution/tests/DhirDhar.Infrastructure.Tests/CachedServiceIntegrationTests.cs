using System;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Caching;
using DhirDhar.Application.Dashboard.Models;
using DhirDhar.Application.Transactions.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Caching;
using DhirDhar.Infrastructure.Dashboard;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using DhirDhar.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class CachedServiceIntegrationTests : IDisposable
{
    private readonly TempDatabase _tempDb;
    private readonly DbContextOptions<DhirDharDbContext> _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cacheService;
    private readonly DashboardService _dashboardService;
    private readonly BorrowerService _borrowerService;

    public CachedServiceIntegrationTests()
    {
        _tempDb = new TempDatabase();
        _options = _tempDb.CreateOptions();

        using (var initContext = new DhirDharDbContext(_options))
        {
            initContext.Database.EnsureCreated();
        }

        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 });
        _cacheService = new MemoryCacheService(memoryCache, NullLogger<MemoryCacheService>.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_cacheService);
        services.AddScoped(_ => new DhirDharDbContext(_options));
        var sp = services.BuildServiceProvider();
        _scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var locService = new LocalizationService();
        _dashboardService = new DashboardService(_scopeFactory, NullLogger<DashboardService>.Instance, locService, _cacheService);
        _borrowerService = new BorrowerService(_scopeFactory, NullLogger<BorrowerService>.Instance, _cacheService);
    }

    public void Dispose()
    {
        _tempDb.Dispose();
    }

    [Fact]
    public async Task DashboardService_CachesSummary_AndInvalidatesOnBorrowerCreate()
    {
        // 1. Initial summary with 0 borrowers
        var summary1 = await _dashboardService.GetSummaryAsync();
        Assert.Equal(0, summary1.TotalBorrowers);

        // 2. Summary should now be cached in _cacheService
        var cached = _cacheService.Get<DashboardSummary>("dashboard_summary");
        Assert.NotNull(cached);
        Assert.Equal(0, cached.TotalBorrowers);

        // 3. Create a borrower via BorrowerService
        var createRequest = new CreateBorrowerRequest(
            "DJ101",
            "Ramesh Patel",
            "Sureshbhai",
            "Patel",
            "Ahmedabad",
            "9876543210",
            "Station Road",
            "123456789012",
            DateTime.Today.AddMonths(-1),
            50000m,
            DateTime.Today.AddMonths(-1),
            "Notes",
            null, null, "Cash", null, null, 2.0m);

        var borrower = await _borrowerService.CreateAsync(createRequest);
        Assert.NotNull(borrower);

        // 4. Cache should have been evicted automatically
        var cachedAfterCreate = _cacheService.Get<DashboardSummary>("dashboard_summary");
        Assert.Null(cachedAfterCreate);

        // 5. Calling GetSummaryAsync fetches fresh count of 1 borrower
        var summary2 = await _dashboardService.GetSummaryAsync();
        Assert.Equal(1, summary2.TotalBorrowers);
    }

    [Fact]
    public async Task BorrowerService_CachesLookupsByIdAndNumber_AndInvalidatesOnUpdate()
    {
        // 1. Create a borrower
        var createRequest = new CreateBorrowerRequest(
            "DJ102",
            "Suresh Kumar",
            "Dineshbhai",
            "Kumar",
            "Surat",
            "9876543211",
            "Ring Road",
            "123456789013",
            DateTime.Today.AddMonths(-2),
            25000m,
            DateTime.Today.AddMonths(-2),
            "Notes",
            null, null, "Cash", null, null, 1.5m);

        var borrower = await _borrowerService.CreateAsync(createRequest);
        var borrowerId = borrower.Id;
        var cleanNumber = borrower.BorrowerNumber.TrimStart('#');

        // 2. Query by ID and by Number (will populate caches)
        var fetchedById = await _borrowerService.GetByIdAsync(borrowerId);
        var fetchedByNum = await _borrowerService.GetByBorrowerNumberAsync(cleanNumber);

        Assert.NotNull(fetchedById);
        Assert.NotNull(fetchedByNum);
        Assert.Equal("Suresh Kumar", fetchedById.Name);

        // 3. Verify caches exist in memory
        Assert.NotNull(_cacheService.Get<BorrowerSummary>($"borrower_id_{borrowerId}"));
        Assert.NotNull(_cacheService.Get<BorrowerSummary>($"borrower_num_{cleanNumber}"));

        // 4. Update borrower details
        var updateRequest = new UpdateBorrowerRequest(
            borrowerId,
            "Suresh Kumar Patel",
            "Dineshbhai",
            "Patel",
            "Surat",
            "9876543211",
            "Ring Road New",
            "123456789013",
            "Updated Notes",
            null, null, "Cash", null, null,
            25000m,
            DateTime.Today.AddMonths(-2),
            1.5m);

        var updated = await _borrowerService.UpdateAsync(updateRequest);
        Assert.Equal("Suresh Kumar Patel", updated.Name);

        // 5. Caches should be invalidated
        Assert.Null(_cacheService.Get<BorrowerSummary>($"borrower_id_{borrowerId}"));
        Assert.Null(_cacheService.Get<BorrowerSummary>($"borrower_num_{cleanNumber}"));

        // 6. Query again returns updated name
        var freshFetch = await _borrowerService.GetByIdAsync(borrowerId);
        Assert.NotNull(freshFetch);
        Assert.Equal("Suresh Kumar Patel", freshFetch.Name);
    }
}
