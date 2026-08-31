using System.Linq.Expressions;

namespace DhirDhar.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Generic repository abstraction over persistent storage. Implementations are provided by
/// the infrastructure layer; domain and application layers depend only on this interface.
/// </summary>
/// <typeparam name="TEntity">The persisted entity type.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class
{
    Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(object id, CancellationToken cancellationToken = default);
}
