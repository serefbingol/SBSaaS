Aşağıdaki içerik, bir önceki **setup\_sbsass.sh** ile oluşturduğun yapıya doğrudan eklenebilecek **paket ekleme komutları**, **Program.cs/DbContext konfigürasyonları**, **Identity özelleştirmesi**, **MinIO dosya servisi**, **Localization ayarları**, **Audit (EF Interceptor + PostgreSQL trigger/SQL alternatifleri)** ve **örnek appsettings.json** şablonlarını içerir. Kod bloklarını ilgili projelere kopyalayarak hızlıca ayağa kaldırabilirsin.

---

# 0) NuGet paketleri – hızlı kurulum komutları

```bash
# Kök klasörde çalıştır
# Infrastructure – EF Core + Npgsql + Identity + MinIO + OpenTelemetry + Serilog
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.EntityFrameworkCore
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Relational
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Minio
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Serilog
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Serilog.Extensions.Hosting
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Serilog.Sinks.Console
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package OpenTelemetry
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package OpenTelemetry.Exporter.Prometheus
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package OpenTelemetry.Extensions.Hosting
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package OpenTelemetry.Instrumentation.AspNetCore
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package OpenTelemetry.Instrumentation.Http

# Application – MediatR + FluentValidation
 dotnet add src/SBSaaS.Application/SBSaaS.Application.csproj package MediatR
 dotnet add src/SBSaaS.Application/SBSaaS.Application.csproj package FluentValidation
 dotnet add src/SBSaaS.Application/SBSaaS.Application.csproj package FluentValidation.DependencyInjectionExtensions

# API – Auth, Localization, Swagger
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.Authentication.Google
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.Authentication.MicrosoftAccount
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Swashbuckle.AspNetCore
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.Extensions.Localization
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.Localization
```

---

# 1) Domain – temel tipler

``

```csharp
namespace SBSaaS.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Culture { get; set; } // örn: "tr-TR"
    public string? UiCulture { get; set; }
    public string? TimeZone { get; set; } // IANA/Windows TZ
}
```

`` (Identity ile kullanılacak)

```csharp
using Microsoft.AspNetCore.Identity;

namespace SBSaaS.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public Guid TenantId { get; set; }
    public string? DisplayName { get; set; }
}
```

---

# 2) Application – sözleşmeler & arayüzler

``

```csharp
namespace SBSaaS.Application.Interfaces;

public interface ITenantContext
{
    Guid TenantId { get; }
}
```

``

```csharp
using System.Threading.Tasks;

namespace SBSaaS.Application.Interfaces;

public interface IFileStorage
{
    Task<string> UploadAsync(string bucket, string objectName, Stream data, string contentType, CancellationToken ct);
    Task<Stream> DownloadAsync(string bucket, string objectName, CancellationToken ct);
    Task DeleteAsync(string bucket, string objectName, CancellationToken ct);
}
```

---

# 3) Infrastructure – DbContext, çok kiracılı global filtre, Audit Interceptor, MinIO servisi

``

```csharp
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Domain.Entities;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Persistence;

public class SbsDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext _tenantContext;

    public SbsDbContext(DbContextOptions<SbsDbContext> options, ITenantContext tenantContext) : base(options)
        => _tenantContext = tenantContext;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(b =>
        {
            b.Property(x => x.TenantId).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique(false);
        });

        // Global query filter örneği: Identity tablolarına doğrudan filtre koymak riskli olabilir,
        // business tablolarında TenantId filtresi uygulayabilirsin. Örnek entity:
        // builder.Entity<YourEntity>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
    }
}
```

``

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
    public DateTime UtcDate { get; set; }
}
```

``

```csharp
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
```

``

```csharp
using Minio;
using Minio.Exceptions;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Storage;

public class MinioFileStorage : IFileStorage
{
    private readonly MinioClient _client;
    public MinioFileStorage(MinioClient client) => _client = client;

    public async Task<string> UploadAsync(string bucket, string objectName, Stream data, string contentType, CancellationToken ct)
    {
        bool found = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct);
        if (!found) await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);

        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType), ct);
        return objectName;
    }

    public Task<Stream> DownloadAsync(string bucket, string objectName, CancellationToken ct)
    {
        var ms = new MemoryStream();
        return Task.Run(async () =>
        {
            await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectName)
                .WithCallbackStream(stream => stream.CopyTo(ms)), ct);
            ms.Position = 0;
            return (Stream)ms;
        }, ct);
    }

    public async Task DeleteAsync(string bucket, string objectName, CancellationToken ct)
        => await _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(objectName), ct);
}
```

``

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Minio;
using SBSaaS.Domain.Entities;
using SBSaaS.Infrastructure.Persistence;
using SBSaaS.Infrastructure.Storage;
using SBSaaS.Application.Interfaces;
using SBSaaS.Infrastructure.Audit;

namespace SBSaaS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<SbsDbContext>(opt =>
        {
            opt.UseNpgsql(config.GetConnectionString("Postgres"));
            opt.EnableSensitiveDataLogging(false);
        });

        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
        {
            o.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<SbsDbContext>()
        .AddDefaultTokenProviders();

        // MinIO client
        services.AddSingleton(sp => new MinioClient()
            .WithEndpoint(config["Minio:Endpoint"]!)
            .WithCredentials(config["Minio:AccessKey"]!, config["Minio:SecretKey"]!)
            .Build());
        services.AddScoped<IFileStorage, MinioFileStorage>();

        services.AddScoped<AuditSaveChangesInterceptor>();

        return services;
    }
}
```

---

# 4) API – Program.cs konfigürasyonu (Identity, OAuth, Localization, Request başına Tenant)

`` (özet)

```csharp
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;
using SBSaaS.Infrastructure;
using SBSaaS.Infrastructure.Persistence;
using SBSaaS.Application.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Tenant Context – basit header temelli örnek (X-Tenant-Id)
builder.Services.AddScoped<ITenantContext, HeaderTenantContext>();

// Localization – varsayılan tr-TR
var supportedCultures = new[] { "tr-TR", "en-US", "de-DE" };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
    options.DefaultRequestCulture = new RequestCulture("tr-TR");
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
});

builder.Services.AddLocalization();

builder.Services.AddAuthentication()
    .AddGoogle(opt =>
    {
        opt.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        opt.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
    })
    .AddMicrosoftAccount(opt =>
    {
        opt.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"]!;
        opt.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"]!;
    });

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// basit health
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// === HeaderTenantContext ===
public class HeaderTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;
    public HeaderTenantContext(IHttpContextAccessor http) => _http = http;
    public Guid TenantId => Guid.TryParse(_http.HttpContext?.Request.Headers["X-Tenant-Id"], out var id) ? id : Guid.Empty;
}
```

> Not: `IHttpContextAccessor` kullanıyorsan `builder.Services.AddHttpContextAccessor();` eklemeyi unutma.

---

# 5) Localization – Resources & örnek kullanım

**Proje yapısı**

```
src/SBSaaS.API/Resources/
  Shared.resx         # varsayılan (tr-TR)
  Shared.en-US.resx
  Shared.de-DE.resx
```

``

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SampleController : ControllerBase
{
    private readonly IStringLocalizer _loc;
    public SampleController(IStringLocalizerFactory factory)
    {
        var type = typeof(SampleController);
        _loc = factory.Create("Shared", System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!);
    }

    [HttpGet("hello")]
    public IActionResult Hello() => Ok(new { message = _loc["Hello"] });
}
```

`Shared.resx` içine `Hello` anahtarı ekle (Türkçe karşılığı), `Shared.en-US.resx` dosyasına İngilizce karşılığı vb.

---

# 6) PostgreSQL – Audit şeması ve tablo SQL (opsiyonel trigger tabanlı alternatif)

**EF ile tablolaştırma** – `ChangeLog` entity’sini `audit.change_log` olarak haritalamak için `OnModelCreating` içine:

```csharp
builder.Entity<SBSaaS.Infrastructure.Audit.ChangeLog>(b =>
{
    b.ToTable("change_log", schema: "audit");
    b.HasKey(x => x.Id);
});
```

**Doğrudan SQL (opsiyonel)** – migration’da çalıştırmak üzere:

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
```

> Dilersen tablo başına trigger ile otomatik loglama da yapabilirsin; fakat EF Interceptor yaklaşımı daha merkezi ve domain bağımsızdır.

---

# 7) Abonelik & Faturalama için şema başlangıcı (öneri)

``

```csharp
namespace SBSaaS.Domain.Entities.Billing;

public class SubscriptionPlan
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;   // PRO, TEAM, ENTERPRISE
    public string Name { get; set; } = default!;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "TRY";
    public bool IsActive { get; set; } = true;
}
```

``

```csharp
namespace SBSaaS.Domain.Entities.Billing;

public class Subscription
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public bool AutoRenew { get; set; }
}
```

> İleride ödeme sağlayıcısına (Iyzico/Stripe/Adyen vb.) adaptör eklemek için `Application` içinde `IPaymentGateway` arayüzü oluşturup `Infrastructure`’da sağlayıcı bazlı implementasyon yazabilirsin.

---

# 8) appsettings.json örneği

``

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=sbsass;Username=postgres;Password=postgres;"
  },
  "Minio": {
    "Endpoint": "localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin"
  },
  "Authentication": {
    "Google": {
      "ClientId": "GOOGLE_CLIENT_ID",
      "ClientSecret": "GOOGLE_CLIENT_SECRET"
    },
    "Microsoft": {
      "ClientId": "MS_CLIENT_ID",
      "ClientSecret": "MS_CLIENT_SECRET"
    }
  },
  "Logging": {
    "LogLevel": { "Default": "Information" }
  }
}
```

---

# 9) Örnek Controller – MinIO yükleme

``

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFileStorage _files;
    public FilesController(IFileStorage files) => _files = files;

    [HttpPost("upload")]
    [Authorize]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromQuery] string bucket="sbs-files")
    {
        if (file == null || file.Length == 0) return BadRequest("file required");
        await using var stream = file.OpenReadStream();
        var name = Guid.NewGuid() + Path.GetExtension(file.FileName);
        await _files.UploadAsync(bucket, name, stream, file.ContentType, HttpContext.RequestAborted);
        return Ok(new { objectName = name, bucket });
    }
}
```

---

# 10) Middleware – Tenant header doğrulama (opsiyonel)

``

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

`Program.cs` içine: `app.UseMiddleware<SBSaaS.API.Middleware.TenantMiddleware>();`

---

# 11) EF Core Migrations – örnek akış

```bash
# design paketlerini gerekirse ekle
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design

# migrations
 dotnet ef migrations add Initial --project src/SBSaaS.Infrastructure --startup-project src/SBSaaS.API
 dotnet ef database update --project src/SBSaaS.Infrastructure --startup-project src/SBSaaS.API
```

---

# 12) Hızlı check-list

-

---
