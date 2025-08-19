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
    private readonly IUserContext _userContext;

    // İsteğe göre PII sözlüğü (tablo.ad -> maske). Attribute yoksa fallback.
    private static readonly Dictionary<string, string> PiiMask = new()
    {
        ["aspnetusers.email"] = "***",
        ["customers.email"] = "***"
    };

    public AuditSaveChangesInterceptor(ITenantContext tenant, IUserContext userContext)
    { _tenant = tenant; _userContext = userContext; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var ctx = eventData.Context; if (ctx is null) return base.SavingChangesAsync(eventData, result, ct);
        var userId = _userContext.UserId;

        var logs = new List<ChangeLog>();
        foreach (var entry in ctx.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            // Denetim logu tablosunun kendisini denetlemeyi atla. Bu, hem gereksizdir hem de
            // potansiyel döngüleri önler.
            if (entry.Entity is ChangeLog)
            {
                continue;
            }

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
                UserId = userId,
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
        var tableName = entry.Metadata.GetTableName()?.ToLowerInvariant();

        foreach (var p in entry.Metadata.GetProperties())
        {
            var clrProp = entry.Entity.GetType().GetProperty(p.Name, BindingFlags.Public | BindingFlags.Instance);

            // 1. PiiAttribute kontrolü (en yüksek öncelik). Bu, tablo adından bağımsız çalışır.
            if (clrProp?.GetCustomAttribute<PiiAttribute>() is { } piiAttr)
            {
                // Attribute'tan özel bir maske değeri geliyorsa onu kullan, yoksa varsayılanı kullan.
                dict[p.Name] = piiAttr.Mask ?? "***";
                continue; // Bu özellik maskelendi, diğer kontrole gerek yok.
            }

            // 2. Sözlük tabanlı kontrol (fallback).
            // Tablo adı veya sütun adı alınamazsa bu kontrol güvenli bir şekilde atlanır.
            if (tableName is null) continue;

            var storeObject = StoreObjectIdentifier.Table(tableName, null);
            var columnName = p.GetColumnName(storeObject)?.ToLowerInvariant();
            if (columnName is null) continue;

            var key = $"{tableName}.{columnName}";
            if (PiiMask.TryGetValue(key, out var mask))
            {
                dict[p.Name] = mask;
            }
        }
        return dict;
    }
}