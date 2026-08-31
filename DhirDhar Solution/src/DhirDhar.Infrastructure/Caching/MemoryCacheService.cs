using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Caching;

public sealed class MemoryCacheService : ICacheService, IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<MemoryCacheService> _logger;
    private readonly ConcurrentDictionary<string, byte> _activeKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public MemoryCacheService(
        IMemoryCache memoryCache,
        ILogger<MemoryCacheService> logger)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        DhirDhar.Infrastructure.Persistence.DhirDharDbContext.OnDatabaseSaved += HandleDatabaseSaved;
    }

    private void HandleDatabaseSaved()
    {
        try
        {
            Remove("dashboard_summary");
            RemoveByPrefix("borrowers_page_");
            RemoveByPrefix("search_query_");
        }
        catch (ObjectDisposedException) { }
    }

    public T? Get<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return default;

        if (_memoryCache.TryGetValue(key, out var cachedValue) && cachedValue is T typedValue)
        {
            return typedValue;
        }

        return default;
    }

    public void Set<T>(string key, T value, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpiration = null)
    {
        if (string.IsNullOrWhiteSpace(key) || value is null) return;

        var options = new MemoryCacheEntryOptions
        {
            SlidingExpiration = slidingExpiration ?? TimeSpan.FromMinutes(2),
            AbsoluteExpirationRelativeToNow = absoluteExpiration ?? TimeSpan.FromMinutes(10),
            Size = 1
        };

        options.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            _activeKeys.TryRemove(evictedKey.ToString() ?? string.Empty, out _);
        });

        _memoryCache.Set(key, value, options);
        _activeKeys.TryAdd(key, 0);
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? slidingExpiration = null,
        TimeSpan? absoluteExpiration = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return await factory(cancellationToken).ConfigureAwait(false);
        }

        if (_memoryCache.TryGetValue(key, out var cachedValue) && cachedValue is T typedValue)
        {
            return typedValue;
        }

        var keyLock = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_memoryCache.TryGetValue(key, out cachedValue) && cachedValue is T secondCheck)
            {
                return secondCheck;
            }

            var freshValue = await factory(cancellationToken).ConfigureAwait(false);
            if (freshValue is not null)
            {
                Set(key, freshValue, slidingExpiration, absoluteExpiration);
            }

            return freshValue;
        }
        finally
        {
            keyLock.Release();
            if (_locks.Count > 1000)
            {
                _locks.TryRemove(key, out _);
            }
        }
    }

    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        _memoryCache.Remove(key);
        _activeKeys.TryRemove(key, out _);
    }

    public void RemoveByPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return;

        var matchingKeys = _activeKeys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in matchingKeys)
        {
            _memoryCache.Remove(key);
            _activeKeys.TryRemove(key, out _);
        }

        _logger.LogDebug("Evicted {Count} cached entries matching prefix '{Prefix}'.", matchingKeys.Count, prefix);
    }

    public void Clear()
    {
        var keys = _activeKeys.Keys.ToList();
        foreach (var key in keys)
        {
            _memoryCache.Remove(key);
            _activeKeys.TryRemove(key, out _);
        }

        _logger.LogDebug("Cleared all {Count} entries from in-memory cache.", keys.Count);
    }

    public void Dispose()
    {
        DhirDhar.Infrastructure.Persistence.DhirDharDbContext.OnDatabaseSaved -= HandleDatabaseSaved;
        Clear();
    }
}
