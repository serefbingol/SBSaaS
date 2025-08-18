using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Audit;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenant;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditSaveChangesInterceptor(ITenantContext tenant, IHttpContextAccessor httpContextAccessor)
    {
        _tenant = tenant;
        _httpContextAccessor = httpContextAccessor;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var ctx = eventData.Context;
        if (ctx == null) return base.SavingChangesAsync(eventData, result, ct);

        var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var logs = new List<ChangeLog>();

        // ChangeLog'un kendisini loglamayı engellemek için filtrele
        var entriesToAudit = ctx.ChangeTracker.Entries()
            .Where(e => e.Entity is not ChangeLog && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

        foreach (var entry in entriesToAudit)
        {
            var table = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name;
            logs.Add(new ChangeLog
            {
                TenantId = _tenant.TenantId,
                TableName = table!,
                KeyValues = JsonSerializer.Serialize(GetKeys(entry)),
                OldValues = entry.State is EntityState.Modified or EntityState.Deleted ? JsonSerializer.Serialize(GetOriginalValues(entry)) : null,
                NewValues = entry.State is EntityState.Added or EntityState.Modified ? JsonSerializer.Serialize(GetCurrentValues(entry)) : null,
                Operation = entry.State.ToString().ToUpperInvariant(),
                UtcDate = DateTime.UtcNow,
                UserId = userId
            });
        }

        if (logs.Any())
        {
            ctx.Set<ChangeLog>().AddRange(logs);
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }

    private static IDictionary<string, object?> GetKeys(EntityEntry entry)
        => entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

    // Güncelleme durumunda sadece değişen özellikleri, silme durumunda tüm özellikleri alır.
    private static IDictionary<string, object?> GetOriginalValues(EntityEntry entry)
        => entry.Properties.Where(p => entry.State == EntityState.Deleted || p.IsModified).ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);

    // Güncelleme durumunda sadece değişen özellikleri, ekleme durumunda tüm özellikleri alır.
    private static IDictionary<string, object?> GetCurrentValues(EntityEntry entry)
        => entry.Properties.Where(p => entry.State == EntityState.Added || p.IsModified).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
}