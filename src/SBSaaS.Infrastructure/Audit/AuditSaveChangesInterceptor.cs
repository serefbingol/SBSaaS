using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Reflection;
using System.Text.Json;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Common.Security;

namespace SBSaaS.Infrastructure.Audit;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenant;
    private readonly IHttpContextAccessor _http;

    // İsteğe göre PII sözlüğü (tablo.ad -> maske). Attribute yoksa fallback.
    private static readonly Dictionary<string, string> PiiMask = new()
    {
        ["aspnetusers.email"] = "***",
        ["customers.email"] = "***"
    };

    public AuditSaveChangesInterceptor(ITenantContext tenant, IHttpContextAccessor http)
    { _tenant = tenant; _http = http; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var ctx = eventData.Context; if (ctx is null) return base.SavingChangesAsync(eventData, result, ct);

        var logs = new List<ChangeLog>();
        foreach (var entry in ctx.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var table = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name;
            var keyJson = JsonSerializer.Serialize(GetKeys(entry));
            var oldJson = entry.State == EntityState.Added ? null : JsonSerializer.Serialize(MaskValues(entry.OriginalValues, entry));
            var newJson = entry.State == EntityState.Deleted ? null : JsonSerializer.Serialize(MaskValues(entry.CurrentValues, entry));

            logs.Add(new ChangeLog
            {
                TenantId = _tenant.TenantId,
                TableName = table!,
                KeyValues = keyJson,
                OldValues = oldJson,
                NewValues = newJson,
                Operation = entry.State.ToString().ToUpperInvariant(),
                UserId = _http.HttpContext?.User?.Identity?.Name,
                UtcDate = DateTimeOffset.UtcNow // Use DateTimeOffset for consistency
            });
        }

        if (logs.Count > 0)
            ctx.Set<ChangeLog>().AddRange(logs);

        return base.SavingChangesAsync(eventData, result, ct);
    }

    private static IDictionary<string, object?> GetKeys(EntityEntry entry)
        => entry.Properties.Where(p => p.Metadata.IsPrimaryKey())
               .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

    private static IDictionary<string, object?> MaskValues(PropertyValues values, EntityEntry entry)
    {
        var dict = values.Properties.ToDictionary(p => p.Name, p => values[p]);
        var table = entry.Metadata.GetTableName()?.ToLowerInvariant();

        foreach (var p in entry.Metadata.GetProperties())
        {
            var clrProp = entry.Entity.GetType().GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance);
            var hasPii = clrProp?.GetCustomAttribute<PiiAttribute>() != null;
            if (hasPii)
            {
                dict[p.Name] = "***"; // attribute maskesi kullanılabilir
                continue;
            }
            var key = $"{table}.{p.GetColumnName(StoreObjectIdentifier.Table(table!, null))}".ToLowerInvariant();
            if (PiiMask.TryGetValue(key, out var mask))
                dict[p.Name] = mask;
        }
        return dict;
    }
}