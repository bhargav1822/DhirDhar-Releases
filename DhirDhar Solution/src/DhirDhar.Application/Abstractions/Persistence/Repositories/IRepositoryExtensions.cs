using System.Linq.Expressions;

namespace DhirDhar.Application.Abstractions.Persistence.Repositories;

public static class IRepositoryExtensions
{
    public static async Task<List<TEntity>> FindAsync<TEntity>(
        this IRepository<TEntity> repository,
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        return await repository.FindAsync(predicate, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<(List<TEntity> Items, int TotalCount)> GetPagedAsync<TEntity>(
        this IRepository<TEntity> repository,
        Expression<Func<TEntity, bool>>? predicate,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        var all = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var query = all.AsQueryable();

        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        var totalCount = query.Count();
        var items = orderBy(query).Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (items, totalCount);
    }
}
