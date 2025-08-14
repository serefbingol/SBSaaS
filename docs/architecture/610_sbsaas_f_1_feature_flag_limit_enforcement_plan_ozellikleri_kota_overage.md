Bu belge **F1 – Feature Flag & Limit Enforcement** iş paketinin uçtan uca uygulanabilir kılavuzudur. Amaç: E1’de tanımlanan plan/özellik (feature) verilerini **koşullu yeteneklere** dönüştürmek; API ve WebApp’te **özellik bayrakları**, **kota/limit** uygulaması, **overage** (aşım ücreti) ve **cache** ile yüksek performanslı karar mekanizması kurmak.

---

# 0) DoD – Definition of Done
- Özellik bayrakları (boolean, numeric, enum) **evaluation service** üzerinden okunuyor (cache destekli).
- API uçları ve UI bileşenleri **policy/attribute** ile özellik/kota kontrolü yapıyor.
- Kota sayaçları (ör. aylık API çağrısı, depolama MB, kullanıcı sayısı) **per-tenant** izleniyor.
- Limit aşımında 429/403 veya **overage** faturası tetikleniyor (isteğe bağlı).
- Observability: limit ihlali ve yakın uyarılar için log/metric/alert var.

---

# 1) Şema Genişletmeleri (billing.*)
```sql
-- Özellik anahtarları E1: billing.feature(key,value) plan düzeyinde.
-- Tenant’a özel override/limit için:
CREATE TABLE IF NOT EXISTS billing.feature_override (
  id bigserial PRIMARY KEY,
  tenant_id uuid NOT NULL,
  key text NOT NULL,
  value text NOT NULL,
  UNIQUE(tenant_id, key)
);

-- Kota sayaçları (rolling window için günlük bucket)
CREATE TABLE IF NOT EXISTS billing.quota_usage (
  id bigserial PRIMARY KEY,
  tenant_id uuid NOT NULL,
  key text NOT NULL,             -- e.g. api_calls, storage_mb, users
  day date NOT NULL,             -- UTC gün
  used bigint NOT NULL DEFAULT 0,
  UNIQUE(tenant_id, key, day)
);

-- Overage kayıtları (opsiyonel faturalama)
CREATE TABLE IF NOT EXISTS billing.overage (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  key text NOT NULL,
  quantity bigint NOT NULL,
  unit_price decimal(10,2) NOT NULL,
  currency char(3) NOT NULL,
  period_start date NOT NULL,
  period_end date NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
```

---

# 2) Feature Evaluation Service
**Amaç:** Plan → Feature → Tenant override zinciri, cache ve tür dönüşümleri.

**`src/SBSaaS.Application/Features/IFeatureService.cs`**
```csharp
public interface IFeatureService
{
    Task<bool> IsEnabledAsync(Guid tenantId, string key, CancellationToken ct);
    Task<int?>  GetIntAsync(Guid tenantId, string key, CancellationToken ct);
    Task<decimal?> GetDecimalAsync(Guid tenantId, string key, CancellationToken ct);
    Task<string?>  GetStringAsync(Guid tenantId, string key, CancellationToken ct);
}
```

**`src/SBSaaS.Infrastructure/Features/FeatureService.cs`** (özet)
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

public class FeatureService : IFeatureService
{
    private readonly SbsDbContext _db; private readonly IMemoryCache _cache;
    public FeatureService(SbsDbContext db, IMemoryCache cache) { _db = db; _cache = cache; }

    private record FeatMap(Dictionary<string,string> Plan, Dictionary<string,string> Override);

    private async Task<FeatMap> LoadAsync(Guid tenantId, CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync(("feat", tenantId), async _ =>
        {
            // aktif planı bul
            var sub = await _db.Subscriptions.Where(x => x.TenantId==tenantId && x.Status=="active")
                        .OrderByDescending(x=>x.StartDate).FirstOrDefaultAsync(ct);
            var planId = sub?.PlanId ?? Guid.Empty;
            var planFeatures = await _db.Features.Where(x=> x.PlanId==planId).ToDictionaryAsync(x=>x.Key, x=>x.Value, ct);
            var overrides = await _db.FeatureOverrides.Where(x=>x.TenantId==tenantId).ToDictionaryAsync(x=>x.Key, x=>x.Value, ct);
            return new FeatMap(planFeatures, overrides);
        })!;
    }

    private static string? Resolve(FeatMap map, string key)
        => map.Override.TryGetValue(key, out var v) ? v : (map.Plan.TryGetValue(key, out var p) ? p : null);

    public async Task<bool> IsEnabledAsync(Guid tenantId, string key, CancellationToken ct)
        => bool.TryParse(Resolve(await LoadAsync(tenantId, ct), key), out var b) && b;
    public async Task<int?> GetIntAsync(Guid tenantId, string key, CancellationToken ct)
        => int.TryParse(Resolve(await LoadAsync(tenantId, ct), key), out var i) ? i : null;
    public async Task<decimal?> GetDecimalAsync(Guid tenantId, string key, CancellationToken ct)
        => decimal.TryParse(Resolve(await LoadAsync(tenantId, ct), key), out var d) ? d : null;
    public async Task<string?> GetStringAsync(Guid tenantId, string key, CancellationToken ct)
        => Resolve(await LoadAsync(tenantId, ct), key);
}
```
**DI**
```csharp
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IFeatureService, FeatureService>();
```

**Cache İptali:** Abonelik/plan/override değişince ilgili `tenantId` anahtarını temizleyin (domain event veya service çağrısı).

---

# 3) Policy/Attribute ile Enforcement (API)
## 3.1 FeaturePolicy
**Anahtar fikir:** `Authorize(Policy="feat:reports")` gibi.

**`FeatureRequirement.cs`**
```csharp
using Microsoft.AspNetCore.Authorization;
public class FeatureRequirement : IAuthorizationRequirement { public string Key { get; } public FeatureRequirement(string key)=>Key=key; }
```
**`FeatureHandler.cs`**
```csharp
using Microsoft.AspNetCore.Authorization;
public class FeatureHandler : AuthorizationHandler<FeatureRequirement>
{
    private readonly ITenantContext _tenant; private readonly IFeatureService _feat;
    public FeatureHandler(ITenantContext t, IFeatureService f){_tenant=t; _feat=f;}
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FeatureRequirement requirement)
    {
        if (_tenant.TenantId==Guid.Empty){ return; }
        if (await _feat.IsEnabledAsync(_tenant.TenantId, requirement.Key, default)) context.Succeed(requirement);
    }
}
```
**`Program.cs`**
```csharp
builder.Services.AddSingleton<IAuthorizationHandler, FeatureHandler>();
builder.Services.AddAuthorization(o=>{
    o.AddPolicy("feat:reports", p=> p.AddRequirements(new FeatureRequirement("reports.enabled")));
});
```
**Kullanım**
```csharp
[Authorize(Policy="feat:reports")]
[HttpGet("/api/v1/reports")] public IActionResult GetReports(){ ... }
```

## 3.2 Kota/Limit Attribute
**`QuotaAttribute.cs`** – örnek: günlük 10.000 API çağrısı
```csharp
[AttributeUsage(AttributeTargets.Method)]
public class QuotaAttribute : Attribute, IAsyncActionFilter
{
    public string Key { get; }
    public int DefaultLimit { get; }
    public QuotaAttribute(string key, int defaultLimit){ Key=key; DefaultLimit=defaultLimit; }

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<SbsDbContext>();
        var feats = ctx.HttpContext.RequestServices.GetRequiredService<IFeatureService>();
        var tenant = ctx.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        var limit = await feats.GetIntAsync(tenant.TenantId, $"quota.{Key}", ctx.HttpContext.RequestAborted) ?? DefaultLimit;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var row = await db.QuotaUsages.SingleOrDefaultAsync(x=>x.TenantId==tenant.TenantId && x.Key==Key && x.Day==today) ?? new QuotaUsage{ TenantId=tenant.TenantId, Key=Key, Day=today, Used=0 };
        if (row.Id==0) db.QuotaUsages.Add(row);

        if (row.Used >= limit)
        {
            ctx.Result = new ObjectResult(new { error="quota_exceeded", key=Key, limit }) { StatusCode = StatusCodes.Status429TooManyRequests };
            return;
        }

        row.Used++;
        await db.SaveChangesAsync();
        await next();
    }
}
```
**Kullanım**
```csharp
[Quota("api_calls", 10000)]
[HttpGet("/api/v1/heavy-resource")] public IActionResult Heavy(){ ... }
```
> **Not:** Yüksek trafik için bu sayaç Redis/PG advisory locks ile atomik tutulmalı. Burada basit PG örneği verildi.

---

# 4) UI (WebApp) – Görünürlük ve Uyarılar
- C3 menü öğelerinde `<authorize policy="feat:reports">` kullanın.
- Limit yaklaşınca banner: `used/limit >= 0.8` → “%80 kotaya ulaştınız.”
- “Planı yükselt” CTA’sı → E3 “Plans” sayfasına link.

---

# 5) Overage (Aşım Ücretlendirme) – Opsiyonel
- Feature anahtarları: `overage.api_calls.unit_price=0.002`, `overage.enabled=true`.
- `QuotaAttribute` aşımda 429 yerine **kaydet & faturalandır** moduna geçebilir:
  - `row.Used++` devam eder; dönem sonunda `row.Used - limit` → `billing.overage` oluştur.
- Uyarı: Müşteri rızası ve sözleşme (G1) şart; UI’da açıkça belirtin.

---

# 6) Rate Limiting & Circuit Breaker (Tamamlayıcı)
- ASP.NET **RateLimiter** middleware ile kullanıcı/tenant bazlı limit; `PartitionedRateLimiter` + `X-Tenant-Id`.
- **Polly** ile dış servis çağrılarında retry/breaker; limit ihlalinde backoff.

---

# 7) Observability
- D2 ile uyumlu metrikler: `quota_used{tenant,key}`, `quota_limit{tenant,key}`, `quota_exceeded_total{tenant,key}`.
- Log: `FeatureDenied`, `QuotaExceeded`, `OverageRecorded` event’leri.

---

# 8) Test Planı
- **Enable/disable**: `reports.enabled=false` iken policy korumalı uç 403 dönmeli.
- **Kota**: Limit 5 iken 6. istek 429; overage modunda fatura kaydı.
- **Override**: Tenant override değeri plan değerini baskılıyor mu?
- **Cache**: Plan değiştikten sonra cache invalidation ile yeni değerler uygulanıyor mu?
- **Concurrency**: Aynı anda 100 istek; sayaç tutarlılığı.

---

# 9) Sonraki Paket
- **F2 – Usage Metering & Billing Sync**: Kota sayaçlarını dönemsel olarak raporlayıp E1 faturalama ile senkron (idempotent) overage faturaları üretme, Stripe metered billing’e opsiyonel köprü.

