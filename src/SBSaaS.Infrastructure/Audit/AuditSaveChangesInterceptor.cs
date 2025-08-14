using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Audit;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenant;
    public AuditSaveChangesInterceptor(ITenantContext tenant) => _tenant = tenant;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var ctx = (DbContext?)eventData.Context;
        if (ctx == null) return base.SavingChangesAsync(eventData, result, ct);

        var logs = new List<ChangeLog>();
        foreach (var entry in ctx.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var table = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name;
            logs.Add(new ChangeLog
            {
                TenantId = _tenant.TenantId,
                TableName = table!,
                KeyValues = JsonSerializer.Serialize(GetKeys(entry)),
                OldValues = entry.State == EntityState.Added ? null : JsonSerializer.Serialize(GetValues(entry.OriginalValues)),
                NewValues = entry.State == EntityState.Deleted ? null : JsonSerializer.Serialize(GetValues(entry.CurrentValues)),
                Operation = entry.State.ToString().ToUpperInvariant(),
                UtcDate = DateTime.UtcNow
            });
        }

        if (logs.Count > 0)
        {
            ctx.Set<ChangeLog>().AddRange(logs);
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }

    private static IDictionary<string, object?> GetKeys(EntityEntry entry)
        => entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

    private static IDictionary<string, object?> GetValues(PropertyValues values)
        => values.Properties.ToDictionary(p => p.Name, p => values[p]);
}