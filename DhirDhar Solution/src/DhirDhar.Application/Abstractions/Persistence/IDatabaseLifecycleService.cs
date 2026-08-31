using System;
using System.Threading;
using System.Threading.Tasks;

namespace DhirDhar.Application.Abstractions.Persistence;

/// <summary>
/// Worker service that performs background database operations and can be safely paused and resumed during database restore.
/// </summary>
public interface IPausableDatabaseWorker
{
    string WorkerName { get; }
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates the application database lifecycle, mutual exclusion between backup and restore,
/// active connection cleanup, inter-process locking, and transactional atomic database replacements with automatic rollback.
/// </summary>
public interface IDatabaseLifecycleService
{
    /// <summary>
    /// Indicates whether exclusive restore mode is currently active across the application.
    /// </summary>
    bool IsRestoreModeActive { get; }

    /// <summary>
    /// Event raised immediately after a database is successfully restored and validated.
    /// Used by ViewModels, caches, and services to refresh application state.
    /// </summary>
    event EventHandler? DatabaseRestored;

    /// <summary>
    /// Acquires exclusive restore lock across threads and processes, pauses background database workers,
    /// and ensures database resources are disposed and released before file replacement.
    /// </summary>
    Task<IDisposable> EnterRestoreModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a safe atomic database replacement from a validated staged database path.
    /// Manages safety backup, atomic swap, integrity check, commit, and automatic rollback on failure.
    /// </summary>
    Task ReplaceDatabaseAtomicallyAsync(string stagedDatabasePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a background service/worker that must be paused during restore mode and resumed after.
    /// </summary>
    void RegisterPausableWorker(IPausableDatabaseWorker worker);
}
