using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class MemoryCacheServiceTests
{
    private MemoryCacheService CreateService(int sizeLimit = 1000)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = sizeLimit
        });
        return new MemoryCacheService(memoryCache, NullLogger<MemoryCacheService>.Instance);
    }

    [Fact]
    public void SetAndGet_ReturnsCachedValue()
    {
        var service = CreateService();

        service.Set("key1", "value1");
        var result = service.Get<string>("key1");

        Assert.Equal("value1", result);
    }

    [Fact]
    public void Get_WhenKeyDoesNotExist_ReturnsDefault()
    {
        var service = CreateService();

        var result = service.Get<string>("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void Remove_EvictsSpecificKey()
    {
        var service = CreateService();

        service.Set("item_1", "value1");
        service.Set("item_2", "value2");

        service.Remove("item_1");

        Assert.Null(service.Get<string>("item_1"));
        Assert.Equal("value2", service.Get<string>("item_2"));
    }

    [Fact]
    public void RemoveByPrefix_EvictsAllMatchingKeys()
    {
        var service = CreateService();

        service.Set("borrower_id_1", "Borrower 1");
        service.Set("borrower_id_2", "Borrower 2");
        service.Set("dashboard_summary", "Dashboard");

        service.RemoveByPrefix("borrower_id_");

        Assert.Null(service.Get<string>("borrower_id_1"));
        Assert.Null(service.Get<string>("borrower_id_2"));
        Assert.Equal("Dashboard", service.Get<string>("dashboard_summary"));
    }

    [Fact]
    public async Task GetOrCreateAsync_ExecutesFactoryOnlyOnceForSameKey()
    {
        var service = CreateService();
        int factoryInvocationCount = 0;

        Task<string> Factory(CancellationToken ct)
        {
            Interlocked.Increment(ref factoryInvocationCount);
            return Task.FromResult("computed_value");
        }

        var result1 = await service.GetOrCreateAsync("factory_key", Factory);
        var result2 = await service.GetOrCreateAsync("factory_key", Factory);

        Assert.Equal("computed_value", result1);
        Assert.Equal("computed_value", result2);
        Assert.Equal(1, factoryInvocationCount);
    }

    [Fact]
    public void Clear_RemovesAllKeys()
    {
        var service = CreateService();

        service.Set("a", 1);
        service.Set("b", 2);
        service.Set("c", 3);

        service.Clear();

        Assert.Null(service.Get<int?>("a"));
        Assert.Null(service.Get<int?>("b"));
        Assert.Null(service.Get<int?>("c"));
    }
}
