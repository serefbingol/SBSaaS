Bu belge **G3 – Developer Portal & Docs** iş paketinin uçtan uca kılavuzudur. Hedef: API‑First stratejisine uygun **geliştirici portalı** kurmak; **OpenAPI 3.1** sözleşmesini (G1) bir portala yayınlamak, **Try‑it** ile canlı deneme (mock + gerçek), **SDK quickstart**’ları (G2) sunmak, sürümleme/değişiklik günlüğü, arama ve analitikle zenginleştirmek.

---

# 0) DoD – Definition of Done
- `docs/` altında portal kaynağı kuruldu ve **CI/CD** ile otomatik yayınlanıyor (GitHub Pages / Vercel / Netlify).
- **Redoc** veya **Stoplight Elements** ile OAS (OpenAPI) render ediliyor.
- **Swagger UI (Try‑it)** gerçek ve **Prism mock** ortamlarına karşı çağrı yapabiliyor.
- **SDK indirme** (NuGet/npm linkleri), **Quickstart**’lar (C# & TS) ve **Auth flow** rehberi mevcut.
- Sürüm dokümantasyonu (v1, v1.1, …), **Changelog**, **deprecation/sunset** notları yayınlanıyor.
- Site araması, koyu/açık tema, i18n (tr/en) ve temel analitik aktif.

---

# 1) Dizim & Araçlar
```
/docs
  /openapi            # OAS kaynakları (tek dosya veya parçalanmış)
  /guides             # quickstart, auth, i18n, tenant, rate-limit, webhook
  /sdks               # .NET/TS kullanım örnekleri
  /changelog
  /assets
  redocly.yaml        # Redocly config (opsiyonel)
  docusaurus.config.js# Docusaurus portal (opsiyonel)
```
**Araçlar:**
- Render: **Redocly** (CLI) **veya** **Stoplight Elements**; yanında **Swagger UI**.
- Mock: **Prism** (`@stoplight/prism-cli`).
- Portal çerçevesi (opsiyonel): **Docusaurus** (React) – i18n ve arama için güçlü.

---

# 2) OpenAPI Yayınlama
## 2.1 Redocly (Statik build)
**`docs/redocly.yaml`**
```yaml
apis:
  sbsaas:
    root: ./openapi/openapi.yaml
lint:
  extends:
    - "spectral:oas"
  rules:
    info-contact: off
referenceDocs:
  theme:
    colors.primary.main: "#0d9488"
  htmlTemplate: ./assets/redoc-template.html
```
**Build**
```bash
npx @redocly/cli build-docs docs/openapi/openapi.yaml -o build/redoc.html
```

## 2.2 Stoplight Elements (Web bileşeni)
**`docs/index.html`** (özet)
```html
<script type="module">
  import '@stoplight/elements/styles.min.css';
  import { API } from '@stoplight/elements';
</script>
<api-document
  apiDescriptionUrl="/openapi/openapi.yaml"
  router="hash"
  layout="sidebar"
></api-document>
```

## 2.3 Swagger UI (Try‑it)
**`docs/swagger.html`** (özet)
```html
<div id="swagger"></div>
<script src="https://unpkg.com/swagger-ui-dist/swagger-ui-bundle.js"></script>
<script>
  window.ui = SwaggerUIBundle({
    url: '/openapi/openapi.yaml',
    dom_id: '#swagger',
    deepLinking: true,
    withCredentials: true,
    requestInterceptor: (req) => {
      const tenant = localStorage.getItem('tenantId');
      if (tenant) req.headers['X-Tenant-Id'] = tenant;
      req.headers['X-Correlation-Id'] = crypto.randomUUID();
      return req;
    }
  });
</script>
```

---

# 3) Mock & Try‑it Ortamı
## 3.1 Prism Mock Service
**Komut**
```bash
npx @stoplight/prism-cli mock docs/openapi/openapi.yaml --port 4010 --cors --errors
```
**Notlar**
- `--errors` ile hata örnekleri de üretilebilir.
- Portalda “Try with Mock” ve “Try with API” düğmeleri.

## 3.2 Ortam Değiştirme (Prod/Staging/Mock)
Portalda **env switcher**:
- `https://api.sbsaas.com` (Prod)
- `https://staging-api.sbsaas.com` (Staging)
- `http://localhost:4010` (Mock)

Swagger UI `preauthorizeApiKey` veya **server** dropdown’ı ile ortam seçimi.

---

# 4) SDK Quickstart’ları (G2 bağlantılı)
## 4.1 .NET (NuGet)
**`docs/guides/quickstart-dotnet.md`**
```md
### Kurulum
```powershell
Install-Package SBSaaS.Client
```
### Başlangıç
```csharp
var services = new ServiceCollection();
services.AddSbsSaaSClient(o => {
  o.BaseUrl = "https://api.sbsaas.com";
  o.TenantIdProvider = () => tenantId; // Guid
  o.BearerTokenProvider = () => token;  // JWT
});
var sp = services.BuildServiceProvider();
var api = sp.GetRequiredService<ISbsApi>();
var plans = await api.BillingListPlansAsync();
```
```

## 4.2 TypeScript (npm)
**`docs/guides/quickstart-ts.md`**
```md
### Kurulum
```bash
npm i @sbsaas/client @sbsaas/client-generated
```
### Başlangıç
```ts
import { createClient } from '@sbsaas/client'
const api = createClient({ baseURL: 'https://api.sbsaas.com', tenantIdProvider: ()=>activeTenantId, tokenProvider: ()=>jwt })
const plans = await api.billing.billingListPlans()
```
```

---

# 5) Auth Flow Rehberi
**`docs/guides/auth.md`**: OAuth2 (Authorization Code + PKCE), cookie auth (WebApp), Bearer (machine‑to‑machine) örnekleri; **tenant eşlemesi**: token claim `tenant_id` ↔ `X‑Tenant‑Id`.

**Postman koleksiyonu** ve **.http** örnekleri (`VS Code REST Client`) eklenir.

---

# 6) Rate‑Limit & Kota Rehberi
**`docs/guides/rate-limit.md`**: F3‑F4 ile uyumlu başlıklar, `Retry‑After`/`X‑RateLimit‑Reset` yorumlama, **best practices** (exponential backoff + jitter), SDK davranışı (G2).

---

# 7) Webhook Rehberi
**`docs/guides/webhooks.md`**: Stripe `Stripe‑Signature` doğrulama, iyzico callback; **idempotency** ve **retry** stratejileri; örnek imza doğrulama kodları.

---

# 8) i18n & Biçimlendirme
**`docs/guides/i18n.md`**: `Accept‑Language` kullanımı, tarih/para birimi biçimlendirme; API’da ISO‑8601 ve sabit ondalıklar (G1 ile uyumlu).

---

# 9) Versiyonlama, Changelog, Deprecation
- **Sürüm klasörleri**: `docs/openapi/v1/openapi.yaml`, `v1.1/…`
- **Changelog**: `docs/changelog/v1.md` – yeni uçlar, kırıcı değişiklikler, `Sunset`/`Deprecation` başlık örnekleri.
- **URL versiyonlama** (`/api/v1`), **SDK semver** eşlemesi: `SDK 1.2.x ↔ OAS v1.2`.

---

# 10) Arama, Tema, Analitik
- **Algolia DocSearch** (Docusaurus) – ücretsiz OSS planı veya kendi indeksiniz.
- Tema: Koyu/Açık; logo, favicon.
- Analitik: **Plausible**/**GA4**; onay çubuğu (çerez politikası).

---

# 11) CI/CD (Docs)
**GitHub Actions – `docs.yml`**
```yaml
name: Docs
on:
  push:
    branches: [ main ]
    paths: [ 'docs/**', 'contracts/openapi.yaml' ]

jobs:
  build-deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Build Redoc
        run: npx @redocly/cli build-docs docs/openapi/openapi.yaml -o build/redoc.html
      - name: Copy Portal
        run: |
          mkdir -p build
          cp -R docs/* build/
      - name: Deploy to Pages
        uses: actions/deploy-pages@v4
        with: { folder: build }
```
> Alternatif: **Vercel/Netlify**; **Prism mock** için ayrı job ve URL (örn. `mock.sbsaas.dev`).

---

# 12) Güvenlik & Üretim Notları
- **Mock** ortamında **gerçek gizli veri** olmayacak; örnek payloadlar anonim.
- Swagger UI “Try‑it” gerçek API’ye çağrı yapıyorsa **CORS** ve **rate‑limit** sınırlarını düşük tutun.
- Portal sadece public bilgileri barındırmalı; admin/internal endpoint dökümantasyonu ayrı alan/SSO altında tutulmalı.

---

# 13) Test Planı
- OAS linter (Spectral) temiz; portal build başarıyla çıkıyor.
- Swagger UI’da env switcher ile **mock** ve **prod** istekleri başarılı.
- SDK quickstart kodları gerçek/mocked API’ye karşı çalışıyor.
- Arama ve tema ayarları (i18n) doğru; kırık link taraması (linkinator) yeşil.

---

# 14) Sonraki Paket
- **G4 – Postman/Insomnia Collections & Examples**: Otomatik koleksiyon üretimi, environment değişkenleri, CI ile senkronizasyon; örnek istek/yanıt koleksiyonları.

