Bu belge **A1** iş paketinin uçtan uca uygulanabilir kılavuzudur. Tek veritabanı + `Tenant_ID` izolasyonu, EF Core yapılandırmaları, global tenant guard, seed verisi ve TimescaleDB/PostGIS opsiyonları dahildir.

---

# 0) Hızlı Özet (DoD)

- EF Core + Npgsql yapılandırıldı, `SbsDbContext` hazır.
- **Tenant guard**: Tüm `ITenantScoped` varlıklar için `X-Tenant-Id` zorunlu ve otomatik set.
- Global query filter veya repository guard ile veri okuma/yazma **sadece aktif tenant** için.
- Başlangıç **Seed**: `System` tenant + örnek domain tablosu (Projects) eklendi.
- Migration’lar çalıştı; bağlantı ve indeksler hazır.
- (Opsiyonel) TimescaleDB & PostGIS etkinleştirme talimatı.

---

# 1) NuGet Paketleri

> Not: Bu liste, A1 iş paketi için gereken temel EF Core ve Identity paketlerini içerir. Projenin tamamında MinIO, Serilog gibi diğer işlevler için ek paketler de olacaktır.

```bash
# Infrastructure (zaten varsa atla)
dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.EntityFrameworkCore
dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Relational
dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
# Identity entegrasyonu için (IdentityDbContext)
dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore
```

---

# 2) Domain Katmanı – Tenant kapsamı ve ortak alanlar

``

```csharp
namespace SBSaaS.Domain.Common;

public interface ITenantScoped
{
    Guid TenantId { get; set; }
}

public abstract class AuditableEntity
{
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
}
```

**Örnek iş varlığı**\
``

```csharp
using SBSaaS.Domain.Common;

namespace SBSaaS.Domain.Entities.Projects;

public class Project : AuditableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = default!;   // unique in tenant
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}
```

**Tenant varlığı**\
`` (daha önce eklenmişti; burada tekrar)

```csharp
namespace SBSaaS.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Culture { get; set; } // e.g. "tr-TR"
    public string? UiCulture { get; set; } // e.g. "tr-TR"
    public string? TimeZone { get; set; } // e.g. "Europe/Istanbul"
}
```

---

# 3) Infrastructure – DbContext ve Tenant Guard

``

```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Entities.Projects;
using SBSaaS.Domain.Common;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Persistence;

public class SbsDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext _tenant;

    public SbsDbContext(DbContextOptions<SbsDbContext> options, ITenantContext tenant)
        : base(options) => _tenant = tenant;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Tenant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        // Global query filter şablonu – ITenantScoped olan tüm entity’lere uygula
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(SbsDbContext)
                    .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                method.MakeGenericMethod(entityType.ClrType)
                      .Invoke(null, new object[] { this, b });
            }
        }
    }

    // Yeni eklenen/updated entity’lerde TenantId ve zaman damgalarını otomatik ata
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var currentTenantId = _tenant.TenantId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditableEntity aud)
            {
                if (entry.State == EntityState.Added) aud.CreatedUtc = now;
                if (entry.State == EntityState.Modified) aud.UpdatedUtc = now;
            }

            if (entry.Entity is ITenantScoped scoped)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        // Eğer TenantId manuel olarak (örn: seeder tarafından) atanmamışsa, bağlamdaki tenant'ı ata.
                        if (scoped.TenantId == Guid.Empty)
                        {
                            scoped.TenantId = currentTenantId;
                        }
                        // Atama denemesinden sonra hala TenantId boş ise bu bir hatadır.
                        if (scoped.TenantId == Guid.Empty)
                        {
                            throw new InvalidOperationException($"Cannot save entity of type {entry.Entity.GetType().Name} without a TenantId.");
                        }
                        break;

                    case EntityState.Modified:
                        // Orijinal TenantId'yi veritabanından geldiği haliyle kontrol et.
                        var originalTenantId = entry.OriginalValues.GetValue<Guid>(nameof(ITenantScoped.TenantId));
                        if (!Equals(originalTenantId, currentTenantId))
                        {
                            throw new InvalidOperationException($"Cross-tenant update attempt detected. Entity belongs to tenant {originalTenantId}.");
                        }

                        // TenantId alanının değiştirilmesini engelle. Bu, bir verinin başka bir tenanta taşınmasını önler.
                        if (entry.Property(nameof(ITenantScoped.TenantId)).IsModified)
                        {
                            throw new InvalidOperationException("Changing the TenantId of an existing entity is not allowed.");
                        }
                        break;

                    case EntityState.Deleted:
                        // Silme işleminde de verinin orijinal sahibinin mevcut tenant olduğunu doğrula.
                        var tenantIdForDelete = entry.OriginalValues.GetValue<Guid>(nameof(ITenantScoped.TenantId));
                        if (!Equals(tenantIdForDelete, currentTenantId))
                        {
                            throw new InvalidOperationException($"Cross-tenant delete attempt detected. Entity belongs to tenant {tenantIdForDelete}.");
                        }
                        break;
                }
            }
        }
        return base.SaveChangesAsync(ct);
    }

    private static void SetTenantFilter<TEntity>(SbsDbContext ctx, ModelBuilder b) where TEntity : class
    {
        b.Entity<TEntity>().HasQueryFilter(e => !typeof(ITenantScoped).IsAssignableFrom(typeof(TEntity)) ||
            ((ITenantScoped)e!).TenantId == ctx._tenant.TenantId);
    }
}
```

> Not: Identity tablolarına **global filter** doğrudan uygulanmıyor. Identity erişimleri servis katmanında tenant kontrollü yapılmalı.

---

# 4) TenantContext ve Middleware

`` (varsa atla)

```csharp
namespace SBSaaS.Application.Interfaces;
public interface ITenantContext { Guid TenantId { get; } }
```

``

```csharp
using SBSaaS.Application.Interfaces;

namespace SBSaaS.API.Infrastructure;

public class HeaderTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;
    public HeaderTenantContext(IHttpContextAccessor http) => _http = http;
    public Guid TenantId => Guid.TryParse(_http.HttpContext?.Request.Headers["X-Tenant-Id"], out var id) ? id : Guid.Empty;
}
```

`` (opsiyonel ama önerilir)

```csharp
namespace SBSaaS.API.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    public TenantMiddleware(RequestDelegate next) => _next = next;
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey("X-Tenant-Id"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "X-Tenant-Id header required" });
            return;
        }
        await _next(context);
    }
}
```

`` (ilgili kayıtlar)

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HeaderTenantContext>();
app.UseMiddleware<SBSaaS.API.Middleware.TenantMiddleware>();
```

---

# 5) Configuration & Connection String

``

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=sbsaasdb;Username=postgres;Password=postgres;"
  }
}
```

`` (DbContext kaydı)

```csharp
services.AddDbContext<SbsDbContext>(opt =>
{
    opt.UseNpgsql(config.GetConnectionString("Postgres"));
    opt.EnableSensitiveDataLogging(false);

    // Not: A2 - Audit Logging iş paketiyle birlikte, denetim kaydı için
    // AuditSaveChangesInterceptor'ın da buraya eklenmesi gerekecektir.
    // Örnek:
    // opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});
```

---

# 6) Seed Mekanizması

``

```csharp
using Microsoft.EntityFrameworkCore;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Entities.Projects;

namespace SBSaaS.Infrastructure.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(SbsDbContext db)
    {
        var systemTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        await db.Database.MigrateAsync();

        if (!await db.Tenants.AnyAsync(t => t.Id == systemTenantId))
        {
            var systemTenant = new Tenant
            {
                Id = systemTenantId,
                Name = "System Tenant",
                Culture = "tr-TR",
                TimeZone = "Europe/Istanbul"
            };
            db.Tenants.Add(systemTenant);
            await db.SaveChangesAsync();
        }

        // Örnek proje kaydı (seed), aktif tenant bağlamı gerektiği için manuel set ediliyor
        if (!await db.Projects.AnyAsync(p => p.TenantId == systemTenantId))
        {
            db.Projects.Add(new Project
            {
                Id = Guid.NewGuid(),
                TenantId = systemTenantId,
                Code = "PRJ-001",
                Name = "Kickoff",
                Description = "Initial seed project"
            });
            await db.SaveChangesAsync();
        }
    }
}
```

`` – başlatırken seed çağrısı

```csharp
using SBSaaS.Infrastructure.Persistence;
using SBSaaS.Infrastructure.Seed;

// ... app.Build() sonrası
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SbsDbContext>();
    await DbSeeder.SeedAsync(db);
}
```

---

# 7) Migration Akışı

```bash
# ilk migration
 dotnet ef migrations add A1_Initial --project src/SBSaaS.Infrastructure --startup-project src/SBSaaS.API --context SbsDbContext
# veritabanını güncelle
 dotnet ef database update --project src/SBSaaS.Infrastructure --startup-project src/SBSaaS.API --context SbsDbContext
```

**Oluşacak tablolar (beklenen)**

- `tenants`
- `projects`
- (Identity tabloları) `aspnetusers`, `aspnetroles`, `aspnetuserroles`, ...

> `audit` şeması A2 paketi ile eklenecektir.

---

# 8) Repository Guard (opsiyonel ek koruma)

``

```csharp
using System.Linq.Expressions;
using SBSaaS.Domain.Common;

namespace SBSaaS.Application.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetAsync(Expression<Func<T, bool>> predicate, CancellationToken ct);
    Task<IReadOnlyList<T>> ListAsync(Expression<Func<T, bool>>? predicate, CancellationToken ct);
    Task<T> AddAsync(T entity, CancellationToken ct);
    Task UpdateAsync(T entity, CancellationToken ct);
    Task DeleteAsync(T entity, CancellationToken ct);
}
```

``

```csharp
using Microsoft.EntityFrameworkCore;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Common;

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
```

---

# 9) Örnek API – Projects Controller (A1 doğrulama)

``

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Infrastructure.Persistence;
using SBSaaS.Application.Common.Models; // Yeni DTO için using
using SBSaaS.Domain.Entities.Projects;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize] // A3 ile netleşecek; şimdilik opsiyonel
public class ProjectsController : ControllerBase
{
    private readonly SbsDbContext _db;
    public ProjectsController(SbsDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<PagedList<Project>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.Projects.AsNoTracking();
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedUtc)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync();
        return Ok(new PagedList<Project>(items, page, pageSize, total));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProjectCreateDto dto)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Code = dto.Code,
            Name = dto.Name,
            Description = dto.Description
            // TenantId, SaveChangesAsync'teki guard tarafından otomatik olarak atanacaktır.
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = project.Id }, project);
    }

    // API sözleşmesi için DTO (Data Transfer Object)
    public record ProjectCreateDto(string Code, string Name, string? Description);
    
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var proj = await _db.Projects.FindAsync(id);
        if (proj == null) return NotFound();
        return Ok(proj);
    }
}
```

> Bu controller, global filter ve SaveChanges guard sayesinde **sadece aktif tenant** verilerini görür.

---

# 10) TimescaleDB & PostGIS (Opsiyonel)

**PostgreSQL eklentilerinin etkinleştirilmesi** (DB admin yetkisiyle):

```sql
CREATE EXTENSION IF NOT EXISTS timescaledb;
CREATE EXTENSION IF NOT EXISTS postgis;
```

**Timeseries tablosu örneği (opsiyonel)**\
``

```csharp
using SBSaaS.Domain.Common;

namespace SBSaaS.Domain.Entities.Metrics;

public class TenantMetric : ITenantScoped
{
    public Guid TenantId { get; set; }
    public DateTime TimestampUtc { get; set; } // time_bucket için
    public string Key { get; set; } = default!; // e.g. active_users
    public double Value { get; set; }
}
```

Migration’da hypertable dönüşümü (örnek):

```sql
SELECT create_hypertable('tenant_metrics', 'timestamp_utc', if_not_exists => TRUE);
```

**Spatial alan örneği (opsiyonel)**

```sql
-- PostGIS ile geometry/geography alanları için örnek
-- CREATE INDEX ON your_table USING GIST(geom);
```

---

# 11) Test Notları

- `X-Tenant-Id: 11111111-1111-1111-1111-111111111111` header’ı ile `/api/v1/projects` çağrıları tenant izolasyonunu doğrulamalı.
- Başka bir tenant GUID’i ile oluşturulan kayıtlar listede görünmemeli (global filter).
- `Project.Code` aynı tenant içinde tekil, farklı tenantlarda çakışabilir.
- **Çapraz tenant koruması**: Tenant A olarak kimlik doğrulayıp Tenant B'ye ait bir projenin ID'si ile güncelleme/silme denemesi yapıldığında işlemin bir `InvalidOperationException` ile (veya API katmanında 403/404 hatasıyla) engellendiği doğrulanmalı. Bu, `SaveChangesAsync` içindeki guard'ı test eder.

---

# 12) Sonraki Paket

- **A2 – Audit Logging**: `audit.change_log` tablo haritalaması + `SaveChanges` interceptor (PII maskeleme) + sorgu indeksleri + arşivleme.
