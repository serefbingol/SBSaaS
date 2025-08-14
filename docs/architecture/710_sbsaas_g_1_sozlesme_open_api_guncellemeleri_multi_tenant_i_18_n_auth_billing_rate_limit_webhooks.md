Bu belge **G1 – Sözleşme & OpenAPI Güncellemeleri** iş paketini kapsar. Amaç: API‑First yaklaşımına uygun olarak **çok kiracılı** sözleşme (headers, hatalar, limitler), **i18n**, **kimlik/doğrulama**, **billing & payments**, **feature/usage** ve **webhook** uçlarını tek bir OpenAPI 3.1 şemasıyla tanımlamak ve tüketiciler için anlaşılır sözleşme kuralları üretmektir.

---

# 0) DoD – Definition of Done
- `contracts/openapi.yaml` 3.1 şema güncellendi; linter (Spectral) hatasız.
- Ortak sözleşme kuralları: `X-Tenant-Id`, `X-Correlation-Id`, `Accept-Language`, `X-RateLimit-*` response başlıkları.
- Standart hata modeli ve sayfalama şeması eklendi.
- Auth şemaları: **OAuth2 Authorization Code** (WebApp), **JWT Bearer** (API‑to‑API), **API Key** (internal/webhook doğrulama için opsiyonel).
- Billing (E1), Payments (E2), Customer UI (E3), Feature/Quota (F1‑F2), Edge limits (F3‑F4) ile uyumlu endpoint tanımları yer aldı.

---

# 1) Global Sözleşme Kuralları
## 1.1 Zorunlu/Önerilen Başlıklar
- **İstek**
  - `X-Tenant-Id` (UUID, zorunlu – çok kiracılı izolasyon)
  - `X-Correlation-Id` (UUID, önerilen – izlenebilirlik)
  - `Accept-Language` (`tr-TR|en-US|de-DE`, önerilen – i18n)
- **Yanıt**
  - `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` (F3)
  - `Retry-After` (429 durumunda)

**OpenAPI components** (özet):
```yaml
openapi: 3.1.0
info:
  title: SBSaaS Public API
  version: 1.0.0
servers:
  - url: https://api.sbsaas.com
components:
  parameters:
    XTenantId:
      name: X-Tenant-Id
      in: header
      required: true
      description: Aktif kiracının UUID değeri
      schema: { type: string, format: uuid }
    XCorrelationId:
      name: X-Correlation-Id
      in: header
      required: false
      schema: { type: string, format: uuid }
    AcceptLanguage:
      name: Accept-Language
      in: header
      required: false
      schema: { type: string, enum: [tr-TR, en-US, de-DE] }
  responses:
    RateLimited:
      description: Too Many Requests
      headers:
        X-RateLimit-Limit: { schema: { type: integer } }
        X-RateLimit-Remaining: { schema: { type: integer } }
        X-RateLimit-Reset: { schema: { type: integer, description: epoch seconds } }
        Retry-After: { schema: { type: integer, description: seconds } }
      content:
        application/json:
          schema: { $ref: '#/components/schemas/Error' }
  schemas:
    Error:
      type: object
      required: [error, message]
      properties:
        error: { type: string, example: quota_exceeded }
        message: { type: string }
        code: { type: integer }
        details: { type: object, additionalProperties: true }
    PageMeta:
      type: object
      required: [page, pageSize, total]
      properties:
        page: { type: integer, minimum: 1 }
        pageSize: { type: integer, minimum: 1 }
        total: { type: integer, minimum: 0 }
    Page:
      type: object
      properties:
        meta: { $ref: '#/components/schemas/PageMeta' }
        items:
          type: array
          items: { anyOf: [ {type: object} ] }
```

---

# 2) Güvenlik Şemaları (Auth)
```yaml
components:
  securitySchemes:
    OAuth2:
      type: oauth2
      description: Authorization Code (PKCE) – WebApp kullanıcıları
      flows:
        authorizationCode:
          authorizationUrl: https://auth.sbsaas.com/oauth2/authorize
          tokenUrl: https://auth.sbsaas.com/oauth2/token
          scopes:
            openid: OpenID scope
            profile: Basic profile
            email: Email
    Bearer:
      type: http
      scheme: bearer
      bearerFormat: JWT
    ApiKey:
      type: apiKey
      in: header
      name: X-Api-Key
```
Global güvenlik (örnek):
```yaml
security:
  - Bearer: []
```
Belirli uçlarda `OAuth2` veya `ApiKey` kullanılabilir (ör. admin, webhook).

---

# 3) Ortak Hata ve Sayfalama Kullanımı
```yaml
paths:
  /api/v1/resource:
    get:
      parameters:
        - $ref: '#/components/parameters/XTenantId'
        - $ref: '#/components/parameters/AcceptLanguage'
      responses:
        '200':
          description: OK
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Page' }
        '429': { $ref: '#/components/responses/RateLimited' }
        '400':
          description: Bad Request
          content: { application/json: { schema: { $ref: '#/components/schemas/Error' } } }
```

---

# 4) Billing & Plans (E1)
Şema özetleri:
```yaml
components:
  schemas:
    Plan:
      type: object
      required: [id, code, name, price, currency, billingCycle]
      properties:
        id: { type: string, format: uuid }
        code: { type: string }
        name: { type: string }
        description: { type: string, nullable: true }
        price: { type: number, format: decimal }
        currency: { type: string, minLength: 3, maxLength: 3 }
        billingCycle: { type: string, enum: [monthly, yearly] }
        features:
          type: object
          additionalProperties: { type: string }
    Subscription:
      type: object
      required: [id, tenantId, planCode, status, startDate]
      properties:
        id: { type: string, format: uuid }
        tenantId: { type: string, format: uuid }
        planCode: { type: string }
        status: { type: string, enum: [active, cancelled, expired] }
        startDate: { type: string, format: date }
        endDate: { type: string, format: date, nullable: true }
```
Paths (özet):
```yaml
paths:
  /api/v1/billing/plans:
    get:
      summary: Plan listesi
      parameters: [ { $ref: '#/components/parameters/XTenantId' } ]
      responses:
        '200': { description: OK, content: { application/json: { schema: { type: array, items: { $ref: '#/components/schemas/Plan' } } } } }
    post:
      summary: Plan oluştur (admin)
      security: [ { Bearer: [] } ]
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/Plan' }
      responses:
        '201': { description: Created }
```

---

# 5) Payments & Webhooks (E2)
Checkout başlatma ve webhooklar:
```yaml
components:
  schemas:
    StartCheckoutRequest:
      type: object
      required: [planCode, email]
      properties:
        planCode: { type: string }
        email: { type: string, format: email }
    StartCheckoutResponse:
      type: object
      required: [url]
      properties:
        url: { type: string, format: uri }
paths:
  /api/v1/pay/checkout:
    post:
      summary: Checkout başlat
      parameters: [ { $ref: '#/components/parameters/XTenantId' } ]
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/StartCheckoutRequest' }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/StartCheckoutResponse' } } } }
  /webhooks/stripe:
    post:
      summary: Stripe webhook
      security: [ { ApiKey: [] } ] # veya imza başlığı doğrulama notu
      responses: { '200': { description: OK } }
  /webhooks/iyzico/callback:
    post:
      summary: iyzico callback
      requestBody:
        required: true
        content:
          application/x-www-form-urlencoded:
            schema:
              type: object
              properties:
                token: { type: string }
      responses: { '200': { description: OK } }
```
**İmza doğrulama notları** (description’da): Stripe `Stripe-Signature`, iyzico shared secret (varsa).

---

# 6) Customer Billing UI (E3) – API’ler
```yaml
paths:
  /api/v1/billing/invoices:
    get:
      summary: Fatura listesi
      parameters: [ { $ref: '#/components/parameters/XTenantId' } ]
      responses:
        '200':
          content:
            application/json:
              schema:
                allOf:
                  - $ref: '#/components/schemas/Page'
                  - type: object
                    properties:
                      items:
                        type: array
                        items: { $ref: '#/components/schemas/Invoice' }
```
`Invoice`/`Payment` şemaları E1/E2 ile uyumlu şekilde eklenir.

---

# 7) Feature Flags & Quotas (F1‑F2)
Policy & Quota hataları ortak Error şemasıyla döner.
```yaml
components:
  schemas:
    QuotaErrorDetails:
      type: object
      properties:
        key: { type: string }
        limit: { type: integer }
        used: { type: integer }
paths:
  /api/v1/metering/events:
    post:
      summary: Toplu kullanım olayı yaz
      security: [ { Bearer: [] } ]
      parameters: [ { $ref: '#/components/parameters/XTenantId' } ]
      responses: { '202': { description: Accepted } }
```

---

# 8) Edge Rate Limit (F3‑F4) – Başlıklar
Tüm public uçlar için response header tanımı:
```yaml
components:
  headers:
    XRateLimitLimit: { schema: { type: integer }, description: Limit }
    XRateLimitRemaining: { schema: { type: integer }, description: Kalan }
    XRateLimitReset: { schema: { type: integer }, description: Epoch seconds }
```
Kullanım (örnek):
```yaml
responses:
  '200':
    description: OK
    headers:
      X-RateLimit-Limit: { $ref: '#/components/headers/XRateLimitLimit' }
      X-RateLimit-Remaining: { $ref: '#/components/headers/XRateLimitRemaining' }
      X-RateLimit-Reset: { $ref: '#/components/headers/XRateLimitReset' }
```

---

# 9) i18n – Yerelleştirme
- `Accept-Language` ile metin/hata mesajlarının lokalize edildiği belirtilir.
- Para/tarih formatı **sunucu** tarafında kültüre göre biçimlenmez; API **ISO‑8601** tarih, **minor units** veya sabit ondalık döner. UI biçimlendirir.
```yaml
components:
  schemas:
    Money:
      type: object
      properties:
        amount: { type: string, pattern: "^\n?\n?-?\\d+(?:\\.\\d{1,4})?$" } # d.p, API sabit string döner
        currency: { type: string, minLength: 3, maxLength: 3 }
```

---

# 10) Versiyonlama & Deprecaton
- URL versiyonlama: `/api/v1/...`.
- Deprecation bildirimi: `Sunset` header + `Deprecation` header (RFC 8594), `Link: <doc>; rel="sunset"`.
- OpenAPI `x‑deprecatedSince`, `x‑sunsetAt` vendor extension’ları.

---

# 11) Örnek Tam Uç – Checkout
```yaml
paths:
  /api/v1/pay/checkout:
    post:
      summary: Checkout başlat (Stripe/iyzico)
      parameters:
        - $ref: '#/components/parameters/XTenantId'
        - $ref: '#/components/parameters/XCorrelationId'
        - $ref: '#/components/parameters/AcceptLanguage'
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/StartCheckoutRequest' }
      responses:
        '200':
          description: OK
          headers:
            X-RateLimit-Limit: { $ref: '#/components/headers/XRateLimitLimit' }
            X-RateLimit-Remaining: { $ref: '#/components/headers/XRateLimitRemaining' }
            X-RateLimit-Reset: { $ref: '#/components/headers/XRateLimitReset' }
          content:
            application/json:
              schema: { $ref: '#/components/schemas/StartCheckoutResponse' }
        '400': { description: Bad Request, content: { application/json: { schema: { $ref: '#/components/schemas/Error' } } } }
        '401': { description: Unauthorized }
        '429': { $ref: '#/components/responses/RateLimited' }
```

---

# 12) Linting & CI Entegrasyonları
- `@stoplight/spectral` custom rules:
  - Zorunlu global başlıklar: `X-Tenant-Id` her **protected** path’te olmalı.
  - Tüm 4xx/5xx yanıtları `Error` şemasıyla uyumlu olmalı.
  - Sayfalı `GET`’lerde `Page` şeması kullanılmalı.
  - `429` olan uçlarda `X-RateLimit-*` header’ları olmalı.
- **Prism** ile mock ve smoke test: `prism mock contracts/openapi.yaml`.

---

# 13) Üretim Notları
- Webhook endpoint’leri rate‑limit dışı bırakılabilir; imza doğrulaması ve IP allowlist zorunlu.
- `X-Tenant-Id` server‑side kaynaklardan da set edilebilir (Backoffice → API); istemci tarafından gönderiliyorsa auth token ile uyum zorunluluğu (token içindeki tenant ile eşleşme).
- PII/EU GDPR notları: Payment webhook payload’larında PII maskeleme ve saklama süresi.

---

# 14) Sonraki Paket
- **G2 – SDK/Client Kitleri**: .NET/TS client oluşturma (`openapi-generator`), örnekler ve quickstart; `X-Tenant-Id`/rate‑limit header’larını otomatik yöneten base client.

