namespace DhirDhar.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Coordinates persistence work within a single unit of work. Repositories created through
/// the unit of work share its database context, enabling multi-operation financial operations
/// to be executed atomically within a single transaction.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IRepository<TEntity> GetRepository<TEntity>()
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
