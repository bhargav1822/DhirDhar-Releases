using System.Linq.Expressions;
using DhirDhar.Application.Abstractions.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DhirDhar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Generic repository implementation backed by Entity Framework Core.
/// </summary>
/// <typeparam name="TEntity">The persisted entity type.</typeparam>
public sealed class Repository<TEntity> : IRepository<TEntity>
    where TEntity : class
{
    private readonly DhirDharDbContext _dbContext;
    private readonly DbSet<TEntity> _dbSet;

    public Repository(DhirDharDbContext dbContext)
    {
        _dbContext = dbContext;
        _dbSet = dbContext.Set<TEntity>();
    }

    public Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        return _dbSet.FindAsync(new[] { id }, cancellationToken).AsTask();
    }

    public Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _dbSet.ToListAsync(cancellationToken);
    }

    public Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return _dbSet.Where(predicate).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        _dbSet.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        _dbSet.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(object id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return entity is not null;
    }
}
