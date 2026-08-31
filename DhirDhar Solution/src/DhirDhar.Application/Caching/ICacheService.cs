using System;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Caching;

/// <summary>
/// Thread-safe in-memory caching service for optimizing UI responsiveness and database queries.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Gets a value from cache if present, otherwise returns default.
    /// </summary>
    T? Get<T>(string key);

    /// <summary>
    /// Sets a value in cache with optional sliding and absolute expiration policies.
    /// </summary>
    void Set<T>(string key, T value, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpiration = null);

    /// <summary>
    /// Gets a value from cache or executes the factory function, caching and returning the result.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? slidingExpiration = null, TimeSpan? absoluteExpiration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an item with the specified key from the cache.
    /// </summary>
    void Remove(string key);

    /// <summary>
    /// Removes all cached entries matching the specified prefix.
    /// </summary>
    void RemoveByPrefix(string prefix);

    /// <summary>
    /// Clears all entries from the in-memory cache.
    /// </summary>
    void Clear();
}
