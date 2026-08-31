using DhirDhar.Application.Abstractions.Persistence.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace DhirDhar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Coordinates persistence work within a single unit of work. Repositories created by the
/// unit of work share the underlying database context, and transactions started here allow
/// future financial operations to be committed or rolled back atomically.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly DhirDharDbContext _dbContext;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    public UnitOfWork(DhirDharDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IRepository<TEntity> GetRepository<TEntity>()
        where TEntity : class
    {
        ThrowIfDisposed();
        return new Repository<TEntity>(_dbContext);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active on this unit of work.");
        }

        _transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_transaction is null)
        {
            throw new InvalidOperationException("No active transaction to commit.");
        }

        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_transaction is null)
        {
            throw new InvalidOperationException("No active transaction to roll back.");
        }

        await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }

        // The database context lifetime is managed by the dependency injection container
        // (scoped); disposing it here could interfere with container-managed disposal.
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
