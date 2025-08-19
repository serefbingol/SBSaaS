using Microsoft.EntityFrameworkCore;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Persistence;

public class EfRepository<T> : IRepository<T> where T : class
{
    private readonly SbsDbContext _db;

    // ITenantContext'e burada ihtiyaç yoktur, çünkü asıl tenant güvenliği
    // SbsDbContext içindeki global query filter ve SaveChangesAsync override'ı
    // tarafından sağlanmaktadır. Bu katman, bu merkezi mantığa güvenir.
    public EfRepository(SbsDbContext db)
    { _db = db; }

    // Okuma işlemleri, DbContext'e tanımlı global tenant filtresine güvenir.
    public async Task<T?> GetAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken ct)
        => await _db.Set<T>().FirstOrDefaultAsync(predicate, ct);

    public async Task<IReadOnlyList<T>> ListAsync(System.Linq.Expressions.Expression<Func<T, bool>>? predicate, CancellationToken ct)
        => await (predicate == null ? _db.Set<T>() : _db.Set<T>().Where(predicate)).ToListAsync(ct);

    // Yazma işlemleri, DbContext.SaveChangesAsync içindeki guard'lara güvenir.
    // Bu guard'lar, TenantId'nin doğru ayarlandığını, değiştirilmediğini ve
    // işlemlerin doğru tenant kapsamında yapıldığını garanti eder.
    public async Task<T> AddAsync(T entity, CancellationToken ct)
    {
        _db.Set<T>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(T entity, CancellationToken ct)
    {
        _db.Set<T>().Update(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(T entity, CancellationToken ct)
    {
        _db.Set<T>().Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
