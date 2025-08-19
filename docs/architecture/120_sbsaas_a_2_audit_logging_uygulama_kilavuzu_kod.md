Bu belge **A2 – Audit Logging** iş paketinin uçtan uca uygulamasını içerir. EF Core SaveChanges interceptor yaklaşımı, `audit.change_log` şeması, PII maskeleme, indeks/partisyon stratejisi, opsiyonel trigger alternatifi, sorgu örnekleri ve API uçları dâhil edilmiştir.

---

# 0) DoD – Definition of Done
- `audit.change_log` tablosu oluşturuldu ve EF ile haritalandı.
- `AuditSaveChangesInterceptor` aktif; INSERT/UPDATE/DELETE olaylarını **tenant**, **kullanıcı**, **tablo**, **key/old/new JSON** ile kaydediyor.
- PII alanlar için **otomatik maskeleme** devrede.
- Tarih, tenant, tablo, operasyon alanlarında **indeksler** mevcut.
- 90 gün üzeri veriler için **arşiv/temizlik** stratejisi belirlendi (pgAgent job veya TimescaleDB drop_chunks).
- `/api/v1/audit/change-log` endpoint’i filtrelerle listeliyor.

---

# 1) NuGet (varsa atla)
```bash
# Zaten eklemiştik; kontrol et
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.EntityFrameworkCore
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
```

---

# 2) Domain – PII işaretleyici attribute (opsiyonel ama önerilir)
**`src/SBSaaS.Domain/Common/Security/PiiAttribute.cs`**
```csharp
namespace SBSaaS.Domain.Common.Security;

[AttributeUsage(AttributeTargets.Property)]
public sealed class PiiAttribute : Attribute
{
    public string? Mask { get; init; } // örn: ****@domain.com
}
```

Örnek kullanım (kullanıcı email vb.):
```csharp
using SBSaaS.Domain.Common.Security;

public class Customer
{
    public Guid Id { get; set; }
    [Pii(Mask = "***")] public string Email { get; set; } = default!;
}
```

> Attribute yoksa da **konfig sözlüğü**yle maskeleme yapacağız (bkz. Interceptor).

---

# 3) Infrastructure – Entity & Mapping
**`src/SBSaaS.Infrastructure/Audit/ChangeLog.cs`**
```csharp
namespace SBSaaS.Infrastructure.Audit;

public class ChangeLog
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public string TableName { get; set; } = default!;
    public string KeyValues { get; set; } = default!;   // JSON
    public string? OldValues { get; set; }              // JSON
    public string? NewValues { get; set; }              // JSON
    public string Operation { get; set; } = default!;   // INSERT/UPDATE/DELETE
    public string? UserId { get; set; }
    public DateTimeOffset UtcDate { get; set; }
}
```

**`src/SBSaaS.Infrastructure/Persistence/SbsDbContext.cs`** – mapping ekle
```csharp
// OnModelCreating içinde
builder.Entity<SBSaaS.Infrastructure.Audit.ChangeLog>(b =>
{
    b.ToTable("change_log", schema: "audit");
    b.HasKey(x => x.Id);
    b.Property(x => x.TableName).HasMaxLength(128);
    b.Property(x => x.Operation).HasMaxLength(16);
    b.HasIndex(x => new { x.TenantId, x.UtcDate });
    b.HasIndex(x => new { x.TableName, x.UtcDate });
    b.HasIndex(x => x.Operation); // DoD uyumu ve sorgu performansı için eklendi.
});
```

**SQL alternatif (Migration’a eklenebilir)**
```sql
CREATE SCHEMA IF NOT EXISTS audit;
CREATE TABLE IF NOT EXISTS audit.change_log (
  id bigserial PRIMARY KEY,
  tenant_id uuid NOT NULL,
  table_name text NOT NULL,
  key_values jsonb NOT NULL,
  old_values jsonb,
  new_values jsonb,
  operation text NOT NULL,
  user_id text,
  utc_date timestamp without time zone NOT NULL DEFAULT (now() at time zone 'utc')
);
CREATE INDEX IF NOT EXISTS ix_audit_tenant_date ON audit.change_log (tenant_id, utc_date DESC);
CREATE INDEX IF NOT EXISTS ix_audit_table_date  ON audit.change_log (table_name, utc_date DESC);
```

---

# 4) Interceptor – SaveChanges yakalama + PII maskeleme
**`src/SBSaaS.Infrastructure/Audit/AuditSaveChangesInterceptor.cs`**
```csharp
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        var user = _http.HttpContext?.User;

        // Denetim için kullanıcı kimliğini al. Öncelik 'sub'/'nameidentifier' claim'i, fallback 'name'.
        var userId = user?.Identity?.IsAuthenticated == true
            ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name
            : null;

        var logs = new List<ChangeLog>();
        // ChangeLog entity'sinin kendisini loglamayı atla (sonsuz döngü önlemi).
        foreach (var entry in ctx.ChangeTracker.Entries().Where(e => e.Entity is not ChangeLog && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
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
                UserId = userId,
                UtcDate = DateTimeOffset.UtcNow
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
```

**DI kaydı** – `src/SBSaaS.Infrastructure/DependencyInjection.cs`
```csharp
services.AddHttpContextAccessor();
services.AddScoped<AuditSaveChangesInterceptor>();
services.AddDbContext<SbsDbContext>((sp, opt) =>
{
    opt.UseNpgsql(config.GetConnectionString("Postgres"));
    opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});
```

---

# 5) API – Audit Controller
**`src/SBSaaS.API/Controllers/AuditController.cs`**
```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Infrastructure.Audit;
using SBSaaS.Infrastructure.Persistence;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly SbsDbContext _db;
    public AuditController(SbsDbContext db) => _db = db;

    [HttpGet("change-log")]
    public async Task<IActionResult> Query([FromQuery] DateTime? from, [FromQuery] DateTime? to,
                                           [FromQuery] string? table, [FromQuery] string? operation,
                                           [FromQuery] string? userId,
                                           [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = _db.Set<ChangeLog>().AsNoTracking();
        if (from is not null) q = q.Where(x => x.UtcDate >= from);
        if (to   is not null) q = q.Where(x => x.UtcDate <= to);
        if (!string.IsNullOrWhiteSpace(table)) q = q.Where(x => x.TableName == table);
        if (!string.IsNullOrWhiteSpace(operation)) q = q.Where(x => x.Operation == operation);
        if (!string.IsNullOrWhiteSpace(userId)) q = q.Where(x => x.UserId == userId);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(x => x.UtcDate)
                           .Skip((page - 1) * pageSize)
                           .Take(pageSize)
                           .ToListAsync();
        return Ok(new { items, page, pageSize, total });
    }
}
```

---

# 6) İndeks & Partisyonlama Stratejileri
**Temel indeksler** (zaten tablo oluştururken eklendi):
- `(tenant_id, utc_date DESC)`
- `(table_name, utc_date DESC)`

**Büyük hacim için partisyon** (PostgreSQL native RANGE partitioning):
```sql
-- Ana tabloyu partisyona çevir
ALTER TABLE audit.change_log DETACH PARTITION IF EXISTS audit.change_log_default;
DROP TABLE IF EXISTS audit.change_log_default;

ALTER TABLE audit.change_log PARTITION BY RANGE (utc_date);

-- Aylık partisyon örnekleri
CREATE TABLE IF NOT EXISTS audit.change_log_2025_08 PARTITION OF audit.change_log
FOR VALUES FROM ('2025-08-01') TO ('2025-09-01');
CREATE TABLE IF NOT EXISTS audit.change_log_default PARTITION OF audit.change_log DEFAULT;
```
> Otomasyon için pgAgent ile aylık job yazabilir veya TimescaleDB kullanıyorsan `create_hypertable` + `add_retention_policy` tercih edebilirsin.

**TimescaleDB ile**
```sql
SELECT create_hypertable('audit.change_log', 'utc_date', if_not_exists => TRUE);
SELECT add_retention_policy('audit.change_log', INTERVAL '90 days');
```

---

# 7) Arşiv & Temizlik Stratejileri

Denetim kayıtları zamanla çok büyük boyutlara ulaşabilir. Bu nedenle düzenli bir arşivleme ve temizlik stratejisi kritik öneme sahiptir. İşte birkaç yaklaşım:

## Yaklaşım 1: Partisyon Düşürme (Önerilen)

Eğer 6. bölümde bahsedildiği gibi **TimescaleDB** veya **native PostgreSQL partisyonlama** kullanıyorsanız, en verimli yöntem eski partisyonları (chunk'ları) doğrudan düşürmektir. Bu işlem, satır satır silme (DELETE) yerine dosya sisteminden blokları kaldırdığı için çok daha hızlıdır ve veritabanında şişkinliğe (bloat) yol açmaz.

**TimescaleDB ile:**
`SELECT add_retention_policy('audit.change_log', INTERVAL '90 days');` komutu bu işi otomatik olarak yapar.

**Native Partitioning ile:**
`pgAgent` veya benzeri bir zamanlayıcı ile eski partisyonları `DETACH` edip ardından `DROP TABLE` komutuyla silebilirsiniz.

## Yaklaşım 2: Basit Silme (Küçük Ölçekli Sistemler İçin)

Eğer tablo partisyonlanmamışsa, `pgAgent` gibi bir zamanlayıcı ile periyodik olarak eski kayıtları silebilirsiniz. Bu yöntem, büyük tablolarda yavaş olabilir ve I/O yükü oluşturabilir.

**90 gün üzerini sil:**
```sql
-- DİKKAT: Bu sorgu büyük tablolarda uzun sürebilir ve bloat'a neden olabilir.
DELETE FROM audit.change_log WHERE utc_date < (now() at time zone 'utc' - interval '90 days');
```

> **Arşivleme**: Silmeden önce verileri başka bir `audit_archive.change_log` tablosuna veya S3 gibi bir soğuk depolama alanına taşımak isterseniz, `DELETE` işleminden önce bir `INSERT INTO ... SELECT ...` adımı ekleyebilirsiniz.

---

# 8) Sorgu Örnekleri
```sql
-- Son 24 saatte bir tenant için değişen kayıt sayısı
SELECT table_name, operation, count(*)
FROM audit.change_log
WHERE tenant_id = '11111111-1111-1111-1111-111111111111'
  AND utc_date >= (now() at time zone 'utc' - interval '24 hours')
GROUP BY table_name, operation
ORDER BY 3 DESC;

-- Belirli bir kaydın geçmişi (key JSON araması)
SELECT *
FROM audit.change_log
WHERE table_name = 'projects'
  AND key_values @> '{"Id":"9a2b..."}'::jsonb
ORDER BY utc_date DESC;
-- Not: Bu sorgunun performanslı çalışması için `key_values` sütununda bir GIN indeksi olması gerekir.
```

---

# 9) Testler (özet)
- **Unit**: `MaskValues` PII maskeleme kuralı; attribute ve sözlük birleşimi.
- **Integration**: INSERT/UPDATE/DELETE sonrası audit kaydı oluşuyor mu?
- **Auth entegrasyonu**: `UserId` claim’inden akıyor mu?
- **Performans**: 10k batch update’te interceptor kabul edilebilir süre içinde mi?

---

# 10) OpenAPI güncellemesi (G1 ile senkron)
`/api/v1/audit/change-log` yoluna `from`, `to`, `table`, `operation`, `userId`, `page`, `pageSize` parametreleri eklendi; yanıt `PagedAuditList` şemasına uyuyor.

---

# 11) Sonraki Paket
- **A3 – Identity & Authorization**: JWT, dış sağlayıcılar (Google/Microsoft), tenant-bound kullanıcı/rol/claim yönetimi, invitation flow, rate limit.
