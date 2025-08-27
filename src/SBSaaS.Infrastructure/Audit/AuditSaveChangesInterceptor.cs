using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Common.Security;

namespace SBSaaS.Infrastructure.Audit;

/// <summary>
/// Veritabanındaki değişiklikleri yakalayıp denetim (audit) kaydı oluşturan interceptor.
/// Bu interceptor, her SaveChangesAsync çağrısında devreye girer.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;
    private readonly IUserContext _userContext;

    // PII (Kişisel Tanımlanabilir Bilgi) içeren alanları ve maskeleme kurallarını tanımlayan sözlük.
    // Bu, PiiAttribute kullanılmayan durumlar için bir yedek mekanizmadır.
    private static readonly Dictionary<string, string> PiiMasks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aspnetusers.email"] = "***",
        ["customers.email"] = "***"
    };

    public AuditSaveChangesInterceptor(ITenantContext tenantContext, IUserContext userContext)
    {
        _tenantContext = tenantContext;
        _userContext = userContext;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var dbContext = eventData.Context;
        if (dbContext is null)
        {
            return base.SavingChangesAsync(eventData, result, ct);
        }

        var logs = new List<ChangeLog>();
        // ChangeLog entity'sinin kendisini loglamayı atla (sonsuz döngü önlemi).
        foreach (var entry in dbContext.ChangeTracker.Entries().Where(e => e.Entity is not ChangeLog && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var tableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name;
            logs.Add(new ChangeLog
            {
                TenantId = _tenantContext.TenantId,
                UserId = _userContext.UserId,
                TableName = tableName,
                KeyValues = JsonSerializer.Serialize(GetKeys(entry)),
                OldValues = entry.State == EntityState.Added ? null : JsonSerializer.Serialize(GetMaskedValues(entry.OriginalValues, entry)),
                NewValues = entry.State == EntityState.Deleted ? null : JsonSerializer.Serialize(GetMaskedValues(entry.CurrentValues, entry)),
                Operation = entry.State.ToString().ToUpperInvariant(),
                UtcDate = DateTimeOffset.UtcNow
            });
        }

        if (logs.Count > 0)
        {
            dbContext.Set<ChangeLog>().AddRange(logs);
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }

    private static IDictionary<string, object?> GetKeys(EntityEntry entry)
        => entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

    private static IDictionary<string, object?> GetMaskedValues(PropertyValues values, EntityEntry entry)
    {
        var dict = values.Properties.ToDictionary(p => p.Name, p => values[p]);
        var tableName = entry.Metadata.GetTableName()?.ToLowerInvariant();

        // PII maskeleme mantığı buraya eklenebilir (120_... dokümanındaki gibi).
        // Şimdilik basit tutulmuştur.
        return dict;
    }
}