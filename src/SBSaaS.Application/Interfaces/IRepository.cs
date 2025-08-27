using System.Linq.Expressions;

namespace SBSaaS.Application.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetAsync(Expression<Func<T, bool>> predicate, CancellationToken ct);
    Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>>? predicate, CancellationToken ct);
    Task<T> AddAsync(T entity, CancellationToken ct);
    Task UpdateAsync(T entity, CancellationToken ct);
    Task DeleteAsync(T entity, CancellationToken ct);
}

