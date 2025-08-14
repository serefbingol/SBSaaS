Bu belge **F3 – Quota Enforcement @ Edge** iş paketinin uçtan uca kılavuzudur. Hedef: API girişinde (reverse proxy/gateway) **tenant bazlı oran sınırlama** (rate limit), **patlama/burst kontrolü**, **kota** ve **overage** politikalarının uçta uygulanması; F1 (Policy & Quota) ve F2 (Metering & Billing Sync) ile tutarlı çalışması.

---

# 0) DoD – Definition of Done
- Edge katmanında **Tenant_ID**’ye göre oran sınırlama aktif (X-Tenant-Id/JWT → tenant çıkarımı).
- **Sabit pencereli** (fixed-window) ve **token bucket** (kaynak öneri) stratejileri destekleniyor.
- Limit aşımlarında **429** döndürülüyor; `Retry-After` ve `X-RateLimit-*` başlıkları set ediliyor.
- Limit olayları **OTel** ile izleniyor; F2 `usage_event`e opsiyonel yazım var.
- Konfigürasyon kodla (YARP) ve/veya YAML (NGINX/Envoy) ile yönetiliyor.

---

# 1) Kimlik & Anahtar Çıkarma
Öncelik sırası:
1) **X-Tenant-Id** (C3 handler otomatik ekler)
2) JWT içinden `tenant_id` claim’i (API first senaryosu)
3) IP adresi (fallback – sadece koruma amaçlı)

> Edge’te **X-Tenant-Id** yoksa JWT’yi decode edip claim’den alın. Yoksa IP partition.

---

# 2) Mimarî Seçenekler
- **YARP (.NET Reverse Proxy)**: Uygulama içinde esnek, C# ile politikalar; Redis destekli custom limiter.
- **NGINX**: `limit_req` (leaky bucket benzeri) + `limit_conn`; key: `$http_x_tenant_id` veya JWT’den `map` ile çıkarım.
- **Envoy**: Global/cluster bazlı **Rate Limit Service (RLS)** ile descriptor’lar (tenant key) ve redis-lua.

Hepsi için amaç: **edge’de** isabetli düşürme, API içindeki `[Quota]` (F1) ile **çifte sayım** olmamasını sağlamak (ya edge ya core sayaçlama). Öneri: **Edge = anlık koruma**, **Core = faturalama/kota**.

---

# 3) YARP ile Uygulama
## 3.1 Proje İskeleti
`src/SBSaaS.Gateway` (ASP.NET Core 9 + YARP)
```
Program.cs
appsettings.json
Policies/RateLimitPolicy.cs
Services/ITenantKeyResolver.cs, RedisTokenBucketLimiter.cs
```

**`Program.cs` (özet)**
```csharp
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Yarp.ReverseProxy;

var b = WebApplication.CreateBuilder(args);
b.Services.AddReverseProxy().LoadFromConfig(b.Configuration.GetSection("ReverseProxy"));

b.Services.AddSingleton<ITenantKeyResolver, HeaderJwtTenantResolver>();
b.Services.AddSingleton<IRedisBucket, RedisTokenBucketLimiter>();

b.Services.AddRateLimiter(opt =>
{
    opt.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var tenant = ctx.RequestServices.GetRequiredService<ITenantKeyResolver>().Resolve(ctx);
        return RateLimitPartition.Get("tenant:"+tenant, key =>
            new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 100, // burst
                QueueLimit = 0,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                TokensPerPeriod = 10, // rps
                AutoReplenishment = true
            }));
    });
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = b.Build();
app.UseRateLimiter();
app.MapReverseProxy();
app.Run();
```
> Üretimde **Redis destekli** bir limiter kullanın (aşağıda).

## 3.2 Redis Tabanlı Token Bucket (Dağıtık)
**`RedisTokenBucketLimiter`**: Lua script ile atomik replenish/consume.
- Parametreler: `token_limit`, `refill_rate`, `burst`.
- Anahtar: `rl:{tenant}:{route}` (isteğe bağlı route boyutu).

Pseudocode (Lua):
```lua
-- KEYS[1] = key, ARGV: now_ms, refill_rate, token_limit, cost
-- 1) refill = (now - last)/refill_rate -> add tokens
-- 2) tokens = min(token_limit, tokens + refill)
-- 3) if tokens >= cost then tokens -= cost; ok=1 else ok=0
-- 4) persist state (tokens,last)
```
.NET’te `StackExchange.Redis` ile çağrılır.

## 3.3 Yanıt Başlıkları
Middleware ile ayarla:
```
X-RateLimit-Limit: <limit>
X-RateLimit-Remaining: <kalan>
X-RateLimit-Reset: <epoch seconds>
Retry-After: <seconds>
```

---

# 4) NGINX Konfigürasyonu (Snippet)
**`nginx.conf` (özet)**
```nginx
map $http_x_tenant_id $tenant_key { default $http_x_tenant_id; }
# JWT'den claim çıkarmak için auth_jwt + map fazladan konfigürasyon gerekir.

limit_req_zone $tenant_key zone=tenant_rps:10m rate=10r/s;

server {
  location /api/ {
    limit_req zone=tenant_rps burst=100 nodelay;
    proxy_pass http://api_upstream;
    add_header X-RateLimit-Limit 10 always;
    add_header X-RateLimit-Remaining $limit_req_remaining always;
  }
}
```
> `burst=100` patlama toleransı; `nodelay` kaldırılırsa fazla istekler geciktirilir.

---

# 5) Envoy + Rate Limit Service (RLS)
**Descriptor**
```yaml
descriptors:
- key: tenant
  descriptors:
  - key: id
    rate_limit:
      unit: second
      requests_per_unit: 10
```
**HTTP filter** (özet): tenant id’yi header’dan descriptor’a koy.
RLS olarak **envoyproxy/ratelimit** + Redis kullanılabilir.

---

# 6) F1/F2 ile Tutarlılık
- **Edge (F3)**: yalnız **trafik şekillendirme** ve koruma; 429 düşürür, **faturalama sayacı yazmaz** (opsiyonel).
- **Core (F1/F2)**: **kota/faturalama**; `[Quota]` attribute ve `usage_event`/`usage_period` asıl kaynak.
- İsterseniz F3 429’larını **F2 usage_event** olarak (key: `rate_limit_blocked`) olaylaştırabilirsiniz.

---

# 7) Gözlemlenebilirlik
- OTel: `rate_limit.blocked_total{tenant,route}`, `rate_limit.allowed_total` counter’ları.
- Serilog alanları: `tenantId`, `routeId`, `limit`, `remaining`, `resetAt`.
- Grafana: tenant başına 429 oranı; burst sonrası latency etkisi.

---

# 8) Test Planı
- **Base RPS**: 10 r/s limit; 120 istek/10 sn → ~100 başarılı, ~20 429.
- **Burst**: 100 burst ile kısa süreli patlama kabul ediliyor mu?
- **Tenant izolasyonu**: T1 429 alırken T2 etkilenmiyor mu?
- **Header yok**: `X-Tenant-Id` yoksa IP partition devreye giriyor mu?
- **Headers**: `X-RateLimit-*` ve `Retry-After` doğru set mi? Saat/kayma testleri.
- **Redis outage**: Fail-open/Fail-closed stratejisi (öneri: fail-closed kritik rotalarda fail-open).

---

# 9) Güvenlik & Üretim Notları
- **Header spoofing**: `X-Tenant-Id` yalnız güvenilir ağdan gelmeli; dış istemciden alınıyorsa JWT doğrulaması şart.
- **Bypass**: Sağlık/metrics rotalarını whitelist; admin IP/role için ayrı havuz.
- **Konfig yönetimi**: Limiter parametreleri **plan**/tier ile eşleşmeli (Basic: 5 r/s, Pro: 20 r/s…).
- **Blue/Green**: Limitleri kademeli azalt/artır; 429 ani sıçramaları önlemek için **leaky bucket**/GCRA tercih edilebilir.

---

# 10) Sonraki Paket
- **G1 sözleşme güncelleme**: Planlara edge limit parametrelerini ekle (rps/burst) ve OpenAPI consumer’larına `X-RateLimit-*` başlıklarını belgele.
- **F4 – Self-Service Limit Ayarı (Admin)**: Plan/tenant bazında edge limitlerini UI’dan yönetme, gateway’e hot-reload API’si ile iletme.

