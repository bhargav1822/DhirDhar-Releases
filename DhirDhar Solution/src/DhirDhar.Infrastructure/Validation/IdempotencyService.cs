using System;
using System.Collections.Concurrent;
using DhirDhar.Application.Validation;

namespace DhirDhar.Infrastructure.Validation;

public sealed class IdempotencyService : IIdempotencyService
{
    private sealed record LockEntry(DateTime Expiry, bool Completed);

    private readonly ConcurrentDictionary<string, LockEntry> _cache = new();
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public bool TryAcquireLock(string idempotencyKey, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return true;
        }

        CleanupExpired();
        var key = idempotencyKey.Trim();
        var expiry = DateTime.UtcNow.Add(duration ?? DefaultTtl);

        var acquired = false;
        _cache.AddOrUpdate(
            key,
            _ =>
            {
                acquired = true;
                return new LockEntry(expiry, Completed: false);
            },
            (_, existing) =>
            {
                if (existing.Expiry < DateTime.UtcNow)
                {
                    acquired = true;
                    return new LockEntry(expiry, Completed: false);
                }

                acquired = false;
                return existing;
            });

        return acquired;
    }

    public void ReleaseLock(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        var key = idempotencyKey.Trim();
        _cache.TryRemove(key, out _);
    }

    public bool IsDuplicateSubmission(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return false;
        }

        CleanupExpired();
        var key = idempotencyKey.Trim();
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.Expiry >= DateTime.UtcNow)
            {
                return true;
            }

            _cache.TryRemove(key, out _);
        }

        return false;
    }

    public void RegisterCompleted(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        var key = idempotencyKey.Trim();
        _cache.AddOrUpdate(
            key,
            new LockEntry(DateTime.UtcNow.Add(DefaultTtl), Completed: true),
            (_, existing) => existing with { Completed = true });
    }

    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (k, v) in _cache)
        {
            if (v.Expiry < now)
            {
                _cache.TryRemove(k, out _);
            }
        }
    }
}
