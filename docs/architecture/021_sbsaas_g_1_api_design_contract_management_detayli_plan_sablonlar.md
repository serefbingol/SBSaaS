# G1 – API Design & Contract Management (Detaylı Teknik Plan)

Bu belge, **API First** yaklaşımını uygulamak için atılacak adımları, üretilecek artefaktları ve kabul kriterlerini içerir. Ayrıca başlangıç için **OpenAPI şeması iskeleti**, **Mock API (Prism)** yapılandırması ve **CI kontrol adımları** yer alır.

---

## 1) Hedefler
- Tüm istemciler (Razor WebApp, Mobile, 3rd party) için **tek otorite**: OpenAPI şeması.
- **Versiyonlama** ve **geri uyumluluk** kuralları net biçimde tanımlı.
- Backend/Frontend bağımsız çalışabilsin diye **Mock API**.
- **Postman Collection** + **SDK codegen** için tek kaynak dosya.
- CI/CD’de **şema lint** ve **sözleşme uyumluluğu** denetimleri.

---

## 2) Üretilecek Artefaktlar
- `contracts/openapi.yaml` – OpenAPI 3.1 ana şema
- `contracts/examples/*.json` – Örnek request/response gövdeleri
- `contracts/prism.yml` – Mock API konfigürasyonu (Prism)
- `contracts/postman/SBSaaS.postman_collection.json` – Koleksiyon (otomatik türetilecek)
- `contracts/sdks/` – (opsiyonel) OpenAPI Generator/NSwag çıktıları
- `build/ci/api-contract.yml` – CI job (lint + diff + mock boot smoke)

---

## 3) Versiyonlama Stratejisi
- **Path tabanlı:** `/api/v1/...` (gelecekte `/api/v2` eklenebilir).
- **Sunset/Deprecation**: Eski versiyon uçlarında `Deprecation` ve `Sunset` header’ları.
- `X-Tenant-Id` zorunlu header.

---

## 4) Güvenlik Şeması
- **Bearer JWT** (API için) – `Authorization: Bearer <token>`
- **OAuth2 (authorizationCode)** – Google/Microsoft için `authorizationUrl`, `tokenUrl` platform değerleri (env ile değişebilir)

---

## 5) Kaynaklar (Resources) ve Örnek Uçlar
- **Auth**: `/api/v1/auth/login`, `/auth/external/{provider}/callback`, `/auth/refresh`, `/auth/logout`, `/me`
- **Tenants**: `/api/v1/tenants` (CRUD)
- **Users**: `/api/v1/users` (CRUD, role/claim yönetimi)
- **Files**: `/api/v1/files` (upload, presigned GET/PUT, delete)
- **Subscriptions**: `/api/v1/subscriptions`, `/plans`
- **Audit**: `/api/v1/audit/change-log`

> Tüm çağrılar **tenant kapsamı** ile çalışır; `X-Tenant-Id` ve uygun yetkiler gereklidir.

---

## 6) OpenAPI Şeması – Başlangıç İskeleti (`contracts/openapi.yaml`)
```yaml
openapi: 3.1.0
info:
  title: SBSaaS API
  version: 1.0.0
  description: |
    Multi-tenant SaaS API. All requests must include `X-Tenant-Id`.
servers:
  - url: https://api.sbsass.local/api/v1
    description: Local/Dev
  - url: https://api.sbsass.example.com/api/v1
    description: Production
paths:
  /auth/login:
    post:
      summary: Login with username/password
      tags: [Auth]
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/LoginRequest' }
      responses:
        '200':
          description: OK
          headers:
            Deprecation:
              schema: { type: string }
          content:
            application/json:
              schema: { $ref: '#/components/schemas/AuthTokens' }
        '401': { $ref: '#/components/responses/Unauthorized' }
  /me:
    get:
      summary: Returns current user profile
      tags: [Auth]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
      responses:
        '200':
          description: OK
          content:
            application/json:
              schema: { $ref: '#/components/schemas/UserProfile' }
        '401': { $ref: '#/components/responses/Unauthorized' }
  /tenants:
    get:
      summary: List tenants (admin)
      tags: [Tenants]
      security: [{ bearerAuth: [] }]
      responses:
        '200':
          description: OK
          content:
            application/json:
              schema:
                type: array
                items: { $ref: '#/components/schemas/Tenant' }
    post:
      summary: Create tenant
      tags: [Tenants]
      security: [{ bearerAuth: [] }]
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/TenantCreate' }
      responses:
        '201':
          description: Created
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Tenant' }
  /files:
    post:
      summary: Upload a file to MinIO
      tags: [Files]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
      requestBody:
        required: true
        content:
          multipart/form-data:
            schema:
              type: object
              properties:
                file:
                  type: string
                  format: binary
      responses:
        '200':
          description: OK
          content:
            application/json:
              schema: { $ref: '#/components/schemas/FileObject' }
components:
  securitySchemes:
    bearerAuth:
      type: http
      scheme: bearer
      bearerFormat: JWT
    oauth2:
      type: oauth2
      flows:
        authorizationCode:
          authorizationUrl: https://auth.example.com/authorize
          tokenUrl: https://auth.example.com/token
          scopes:
            openid: OpenID Connect scope
  parameters:
    TenantHeader:
      in: header
      name: X-Tenant-Id
      required: true
      description: Tenant identifier (UUID)
      schema:
        type: string
        format: uuid
  responses:
    Unauthorized:
      description: Unauthorized
      content:
        application/json:
          schema: { $ref: '#/components/schemas/ProblemDetails' }
  schemas:
    ProblemDetails:
      type: object
      properties:
        type: { type: string }
        title: { type: string }
        status: { type: integer }
        detail: { type: string }
        traceId: { type: string }
    LoginRequest:
      type: object
      required: [email, password]
      properties:
        email: { type: string, format: email }
        password: { type: string, format: password }
    AuthTokens:
      type: object
      properties:
        accessToken: { type: string }
        refreshToken: { type: string }
        expiresIn: { type: integer }
    Tenant:
      type: object
      properties:
        id: { type: string, format: uuid }
        name: { type: string }
        culture: { type: string, example: tr-TR }
        timeZone: { type: string, example: Europe/Istanbul }
    TenantCreate:
      type: object
      required: [name]
      properties:
        name: { type: string }
        culture: { type: string }
        timeZone: { type: string }
    UserProfile:
      type: object
      properties:
        id: { type: string }
        email: { type: string }
        displayName: { type: string }
        roles:
          type: array
          items: { type: string }
    FileObject:
      type: object
      properties:
        objectName: { type: string }
        bucket: { type: string }
```

> Not: Burada **Tenants, Users, Subscriptions, Audit** için tüm CRUD uçlarını benzer kalıpla ekleyeceğiz. Şema büyümesin diye kısalttık.

---

## 7) Mock API (Prism) Yapılandırması (`contracts/prism.yml`)
```yaml
# Prism mock configuration
extends: ./openapi.yaml
mock:
  dynamic: true
  errors:
    callback: true
```

**Çalıştırma örneği**
```bash
npm install -g @stoplight/prism-cli
prism mock contracts/openapi.yaml --port 4010
```

---

## 8) Postman Collection Üretimi
- Swagger UI veya `openapi-to-postman` kullanarak koleksiyon türetme:
```bash
npx openapi-to-postmanv2 -s contracts/openapi.yaml -o contracts/postman/SBSaaS.postman_collection.json -p
```

---

## 9) SDK Codegen (Opsiyonel)
- **NSwag** (C# client) veya **OpenAPI Generator** (TS/Java/Kotlin/Swift vb.)
```bash
# Örnek: TypeScript client
npx @openapitools/openapi-generator-cli generate \
  -i contracts/openapi.yaml \
  -g typescript-fetch \
  -o contracts/sdks/typescript
```

---

## 10) CI – Lint & Contract Test (`build/ci/api-contract.yml`)
- **Speccy/Spectral** ile lint
- Şema diff kontrolü (kırıcı değişiklik algılama)
- Mock API smoke testi (ör. `/me` 401 dönmeli)

```yaml
name: api-contract
on: [push, pull_request]
jobs:
  lint-and-mock:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '20' }
      - run: npm install -g @stoplight/spectral-cli @stoplight/prism-cli openapi-diff
      - run: spectral lint contracts/openapi.yaml
      - run: prism mock contracts/openapi.yaml --port 4010 &
      - run: |
          sleep 2
          curl -i http://127.0.0.1:4010/me | grep '401' || (echo 'Mock smoke failed' && exit 1)
```

---

## 11) Kabul Kriterleri (DoD)
- `contracts/openapi.yaml` en az **Auth, Tenants, Files** uçlarını içerir.
- `X-Tenant-Id` zorunluluğu ve **security schemes** tanımlı.
- Prism ile mock servis **çalışır** ve örnek cevaplar döner.
- Postman koleksiyonu **üretilir**.
- CI lint + mock smoke **yeşil**.

---

## 12) Sonraki Adımlar
- **G1 genişletme**: Users, Subscriptions, Audit uçları ve örnekleri ekle.
- **A1 başlat**: Şemaya göre DB tasarımı (entity eşleşmesi, status codes).

