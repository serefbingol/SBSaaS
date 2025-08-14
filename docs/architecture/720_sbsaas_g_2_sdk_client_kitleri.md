Bu belge **G2 – SDK/Client Kitleri** iş paketinin uçtan uca kılavuzudur. Hedef: tek bir OpenAPI 3.1 şemasından **.NET** ve **TypeScript** client’ları üretmek; bu client’lara **X‑Tenant‑Id** başlığını otomatik eklemek, **X‑RateLimit‑*** başlıklarını okuyup akıllı **retry/backoff** uygulamak, **pagination/error** kolaylıkları ve **auth** (Bearer/OAuth2) desteği sağlamak.

---

# 0) DoD – Definition of Done
- `openapi-generator` ile **.NET** ve **TS** client’ları üretildi ve proje içi **wrapper** ile zenginleştirildi.
- Tüm çağrılarda `X-Tenant-Id` otomatik eklendi, `X-Correlation-Id` üretildi.
- 429/503 durumlarında **Retry-After** ve `X-RateLimit-Reset` değerlendirildi; **jit backoff + jitter** ile tekrar denendi.
- `Page` ve `Error` şemaları için yardımcılar eklendi; testler (unit + integration) yeşil.
- Paketler yayımlandı: **NuGet** (`SBSaaS.Client`), **npm** (`@sbsaas/client`).

---

# 1) Repo Yapısı
```
contracts/openapi.yaml
sdks/
  dotnet/
    SBSaaS.Client/           # el yazımı wrapper + handlers
    SBSaaS.Client.Generated/ # generator output (temiz)
  ts/
    packages/
      client/                # el yazımı wrapper (axios interceptors)
      client-generated/      # generator output
```
> **Kural:** Generated klasörlerine **dokunma**. Wrapper projeleriyle genişlet.

---

# 2) OpenAPI Generator Komutları
```bash
# .NET (CSharp)
npx @openapitools/openapi-generator-cli generate \
  -i contracts/openapi.yaml \
  -g csharp \
  -o sdks/dotnet/SBSaaS.Client.Generated \
  --additional-properties=packageName=SBSaaS.Client.Generated,targetFramework=net9.0,optionalEmitDefaultValues=false,validatable=false

# TypeScript (fetch)
npx @openapitools/openapi-generator-cli generate \
  -i contracts/openapi.yaml \
  -g typescript-fetch \
  -o sdks/ts/packages/client-generated \
  --additional-properties=npmName=@sbsaas/client-generated,supportsES6=true,useSingleRequestParameter=true
```
> Alternatif TS: `typescript-axios`. Bu belgede **axios** tabanlı wrapper gösterilecektir.

---

# 3) .NET SDK – Wrapper Tasarımı
## 3.1 Paket Referansı
`sdks/dotnet/SBSaaS.Client/SBSaaS.Client.csproj`
```xml
<ItemGroup>
  <ProjectReference Include="../SBSaaS.Client.Generated/SBSaaS.Client.Generated.csproj" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Http" Version="9.0.0" />
  <PackageReference Include="Polly" Version="8.4.0" />
  <PackageReference Include="Polly.Extensions.Http" Version="3.0.0" />
</ItemGroup>
```

## 3.2 DI Kayıt (consumer uygulamada)
```csharp
services.AddSbsSaaSClient(options =>
{
    options.BaseUrl = "https://api.sbsaas.com";
    options.TenantIdProvider = () => CurrentTenant.Id; // Func<Guid>
    options.BearerTokenProvider = () => session.Jwt;   // Func<string?>
});
```

## 3.3 HttpMessageHandler’lar
**TenantHeaderHandler.cs**
```csharp
public class TenantHeaderHandler : DelegatingHandler
{
    private readonly Func<Guid> _tenant; private readonly Func<string?> _token;
    public TenantHeaderHandler(Func<Guid> tenant, Func<string?> token){ _tenant=tenant; _token = token; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var t = _tenant.Invoke();
        if (t != Guid.Empty) req.Headers.TryAddWithoutValidation("X-Tenant-Id", t.ToString());
        req.Headers.TryAddWithoutValidation("X-Correlation-Id", Guid.NewGuid().ToString());
        var bearer = _token?.Invoke(); if (!string.IsNullOrEmpty(bearer)) req.Headers.Authorization = new("Bearer", bearer);
        return base.SendAsync(req, ct);
    }
}
```
**RateLimitRetryHandler.cs**
```csharp
public class RateLimitRetryHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        for (var attempt=0; attempt<3; attempt++)
        {
            var res = await base.SendAsync(req, ct);
            if ((int)res.StatusCode != 429 && (int)res.StatusCode != 503) return res;
            var retryAfter = res.Headers.RetryAfter?.Delta?.TotalSeconds
                ?? (res.Headers.TryGetValues("X-RateLimit-Reset", out var v) && long.TryParse(v.FirstOrDefault(), out var epoch)
                    ? Math.Max(0, epoch - DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    : 1);
            await Task.Delay(TimeSpan.FromSeconds(retryAfter + Jitter()), ct);
        }
        return await base.SendAsync(req, ct);
    }
    private static double Jitter() => Random.Shared.NextDouble();
}
```

## 3.4 Client Factory
**ServiceCollectionExtensions.cs**
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSbsSaaSClient(this IServiceCollection services, Action<ClientOptions> configure)
    {
        var opts = new ClientOptions(); configure(opts);
        services.AddSingleton(opts);
        services.AddTransient(sp => new TenantHeaderHandler(opts.TenantIdProvider, opts.BearerTokenProvider));
        services.AddTransient<RateLimitRetryHandler>();

        services.AddHttpClient<ISbsApi, SbsApi>(c => c.BaseAddress = new Uri(opts.BaseUrl))
            .AddHttpMessageHandler<TenantHeaderHandler>()
            .AddHttpMessageHandler<RateLimitRetryHandler>()
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(2, retry => TimeSpan.FromMilliseconds(200 * Math.Pow(2, retry) + Random.Shared.Next(0,100))));
        return services;
    }
}
public record ClientOptions
{
    public required string BaseUrl { get; set; }
    public required Func<Guid> TenantIdProvider { get; set; }
    public Func<string?> BearerTokenProvider { get; set; } = () => null;
}
```

## 3.5 Pagination & Error Helpers
```csharp
public static class PageExtensions
{
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(Func<int, Task<Page<T>>> fetch, int startPage=1)
    {
        var page = startPage; Page<T> cur;
        do { cur = await fetch(page++); foreach (var i in cur.Items) yield return i; }
        while (cur.Meta.Page * cur.Meta.PageSize < cur.Meta.Total);
    }
}

public class ApiException : Exception
{
    public int Status { get; }
    public Error? Payload { get; }
    public ApiException(int status, Error? payload, string? message=null) : base(message ?? payload?.Message)
    { Status=status; Payload=payload; }
}
```

---

# 4) TypeScript SDK – Wrapper Tasarımı (axios)
## 4.1 Paket Yapısı
```
sdks/ts/packages/client
  src/
    index.ts
    http.ts          # axios instance + interceptors
    pagination.ts
    errors.ts
  package.json
```

## 4.2 axios Instance & Interceptors
**`http.ts`**
```ts
import axios, { AxiosError, AxiosInstance } from 'axios'

export interface ClientOptions {
  baseURL: string
  tenantIdProvider: () => string | null
  tokenProvider?: () => string | null
}

export function createHttp(opts: ClientOptions): AxiosInstance {
  const http = axios.create({ baseURL: opts.baseURL })

  http.interceptors.request.use(cfg => {
    const tid = opts.tenantIdProvider()
    if (tid) cfg.headers['X-Tenant-Id'] = tid
    cfg.headers['X-Correlation-Id'] = crypto.randomUUID()
    const token = opts.tokenProvider?.()
    if (token) cfg.headers['Authorization'] = `Bearer ${token}`
    return cfg
  })

  http.interceptors.response.use(undefined, async (error: AxiosError) => {
    const res = error.response
    if (!res) throw error
    if (res.status === 429 || res.status === 503) {
      const retryAfter = Number(res.headers['retry-after'])
      const reset = Number(res.headers['x-ratelimit-reset'])
      const wait = !isNaN(retryAfter) ? retryAfter : Math.max(0, reset - Math.floor(Date.now()/1000))
      if ((error.config as any).__retryCount >= 2) throw error
      ;(error.config as any).__retryCount = ((error.config as any).__retryCount || 0) + 1
      await new Promise(r => setTimeout(r, (wait + Math.random()) * 1000))
      return http.request(error.config!)
    }
    throw error
  })

  return http
}
```

## 4.3 Generated Client Entegrasyonu
`client-generated` paketi **fetch** tabanlı ise, wrapper’da **adapter** fonksiyonları sağlayın veya generator’ı `typescript-axios` ile üretip yukarıdaki instance’ı kullanın. Örnek axios yaklaşımı:
```bash
npx @openapitools/openapi-generator-cli generate \
  -i contracts/openapi.yaml -g typescript-axios \
  -o sdks/ts/packages/client-generated \
  --additional-properties=npmName=@sbsaas/client-generated,withSeparateModelsAndApi=true
```
**`index.ts`**
```ts
import { createHttp, ClientOptions } from './http'
import { Configuration, BillingApi, PaymentsApi } from '@sbsaas/client-generated'

export function createClient(opts: ClientOptions) {
  const http = createHttp(opts)
  const cfg = new Configuration({ basePath: opts.baseURL })
  return {
    billing: new BillingApi(cfg, undefined, http),
    payments: new PaymentsApi(cfg, undefined, http),
  }
}
```

## 4.4 Pagination & Errors
**`pagination.ts`**
```ts
export async function* readAll<T>(fetch: (page: number)=>Promise<{ meta:{page:number,pageSize:number,total:number}, items:T[] }>, start=1) {
  let page = start
  while (true) {
    const cur = await fetch(page++)
    for (const i of cur.items) yield i
    if (cur.meta.page * cur.meta.pageSize >= cur.meta.total) break
  }
}
```
**`errors.ts`** — `Error` şemasını yakalayıp anlamlı mesaj döndürür.

---

# 5) Örnek Kullanım
## 5.1 .NET
```csharp
var svc = sp.GetRequiredService<ISbsApi>();
await foreach (var inv in PageExtensions.ReadAllAsync<Invoice>(p => svc.BillingListInvoicesAsync(page: p)))
{
   Console.WriteLine($"{inv.Number} - {inv.Amount} {inv.Currency}");
}
```

## 5.2 TypeScript
```ts
const client = createClient({ baseURL: 'https://api.sbsaas.com', tenantIdProvider: () => activeTenantId, tokenProvider: () => session.jwt })
for await (const inv of readAll((p)=> client.billing.billingListInvoices({ page: p }))) {
  console.log(inv.number, inv.amount, inv.currency)
}
```

---

# 6) Yayınlama & Versiyonlama
## 6.1 .NET (NuGet)
- `SBSaaS.Client.Generated` → **internal** package (opsiyonel) veya aynı feed.
- `SBSaaS.Client` → **public** package.
- CI: `dotnet pack` + `dotnet nuget push` (GitHub Packages veya nuget.org). SemVer **OpenAPI tag**’iyle senkron (`v1.2.0`).

## 6.2 TS (npm)
- `packages/client-generated` ve `packages/client` ayrı paketler.
- CI: `npm version` + `npm publish --access public` (npm veya GitHub Packages).

**CI adımı**: OpenAPI değişince otomatik **regen → build → test → publish** (onaylı).

---

# 7) Test Planı
- **Header’lar**: `X-Tenant-Id` ve `X-Correlation-Id` her istekte var mı?
- **Auth**: Bearer token’lı isteklerde `401→refresh→retry` (opsiyonel) akışı.
- **Rate limit**: 429/503 test doubles → `Retry-After`/`X-RateLimit-Reset` ile retry.
- **Pagination**: büyük listelerde `readAll` tüm öğeleri tüketiyor mu?
- **Regression**: Generator güncellemelerinde wrapper uyumluluğu (snapshot/unit).

---

# 8) Güvenlik & Üretim Notları
- Tenant spoofing’i önlemek için server-side enforced tenant doğrulaması (token içi tenant claim) önerilir.
- `X-Correlation-Id`’yi log/trace’lere işle (D2).
- Paketlerde gizli anahtar bulunmamalı; örnek kodlarda **dummy** değerler.

---

# 9) Sonraki Paket
- **G3 – Developer Portal & Docs**: Redoc/Stoplight Studio ile portal, SDK quickstart’lar, örnek uygulamalar ve canlı **mock** (Prism) ortamı; `Try it` entegrasyonu.

