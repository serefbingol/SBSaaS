Aşağıdaki içerik, G1 paketini genişletir: **Users, Subscriptions (Plans)** ve **Audit** uçları, ortak bileşenler (pagination, arama/sıralama, hata sözlüğü) ve Prism mock örnekleri eklenmiştir. Dosyayı `contracts/openapi.yaml` olarak kullanabilir veya mevcut dosyanı bu bölümlerle genişletebilirsin.

---

# 1) OpenAPI 3.1 – Genişletilmiş Şema

```yaml
openapi: 3.1.0
info:
  title: SBSaaS API
  version: 1.0.0
  description: |
    Multi-tenant SaaS API. All requests must include `X-Tenant-Id` unless specified.
servers:
  - url: https://api.sbsass.local/api/v1
    description: Local/Dev
  - url: https://api.sbsass.example.com/api/v1
    description: Production

paths:
  /auth/login:
    post:
      summary: Login with username/password
      operationId: login
      tags: [Auth]
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/LoginRequest' }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/AuthTokens' } } } }
        '401': { $ref: '#/components/responses/Unauthorized' }
  /auth/refresh:
    post:
      summary: Refresh access token
      operationId: refreshToken
      tags: [Auth]
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/RefreshTokenRequest' }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/AuthTokens' } } } }
        '401': { $ref: '#/components/responses/Unauthorized' }
  /auth/logout:
    post:
      summary: Logout and invalidate refresh token
      operationId: logout
      tags: [Auth]
      security: [{ bearerAuth: [] }]
      responses:
        '204': { description: No Content }
        '401': { $ref: '#/components/responses/Unauthorized' }

  /me:
    get:
      summary: Returns current user profile
      operationId: getMyProfile
      tags: [Auth]
      security: [{ bearerAuth: [] }]
      parameters: [ { $ref: '#/components/parameters/TenantHeader' } ]
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/UserProfile' } } } }
        '401': { $ref: '#/components/responses/Unauthorized' }

  /tenants:
    get:
      summary: List tenants (admin)
      operationId: listTenants
      tags: [Tenants]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/Page'
        - $ref: '#/components/parameters/PageSize'
        - $ref: '#/components/parameters/Query'
        - $ref: '#/components/parameters/Sort'
      responses:
        '200':
          description: OK
          content:
            application/json:
              schema: { $ref: '#/components/schemas/PagedTenantList' }
    post:
      summary: Create tenant
      operationId: createTenant
      tags: [Tenants]
      security: [{ bearerAuth: [] }]
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/TenantCreate' } } }
      responses:
        '201':
          description: Created
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Tenant' }

  /tenants/{id}:
    get:
      summary: Get tenant by id
      operationId: getTenantById
      tags: [Tenants]
      security: [{ bearerAuth: [] }]
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/Tenant' } } } }
        '404': { $ref: '#/components/responses/NotFound' }
    put:
      summary: Update tenant
      operationId: updateTenant
      tags: [Tenants]
      security: [{ bearerAuth: [] }]
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/TenantUpdate' } } }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/Tenant' } } } }
        '404': { $ref: '#/components/responses/NotFound' }
    delete:
      summary: Delete tenant
      operationId: deleteTenant
      tags: [Tenants]
      security: [{ bearerAuth: [] }]
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '204': { description: No Content }
        '404': { $ref: '#/components/responses/NotFound' }

  # ---------------- USERS ----------------
  /users:
    get:
      summary: List users in tenant
      operationId: listUsers
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - $ref: '#/components/parameters/Page'
        - $ref: '#/components/parameters/PageSize'
        - $ref: '#/components/parameters/Query'
        - $ref: '#/components/parameters/Sort'
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/PagedUserList' } } } }
    post:
      summary: Create user in tenant
      operationId: createUser
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters: [ { $ref: '#/components/parameters/TenantHeader' } ]
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/UserCreate' } } }
      responses:
        '201': { description: Created, content: { application/json: { schema: { $ref: '#/components/schemas/User' } } } }

  /users/{id}:
    get:
      summary: Get user by id
      operationId: getUserById
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/User' } } } }
        '404': { $ref: '#/components/responses/NotFound' }
    put:
      summary: Update user
      operationId: updateUser
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/UserUpdate' } } }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/User' } } } }
        '404': { $ref: '#/components/responses/NotFound' }
    delete:
      summary: Delete user
      operationId: deleteUser
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '204': { description: No Content }
        '404': { $ref: '#/components/responses/NotFound' }

  /users/{id}/roles:
    put:
      summary: Replace user roles
      operationId: setUserRoles
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/RoleAssignment' } } }
      responses:
        '204': { description: No Content }

  # -------------- SUBSCRIPTIONS & PLANS --------------
  /plans:
    get:
      summary: List subscription plans
      operationId: listPlans
      tags: [Subscriptions]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/Page'
        - $ref: '#/components/parameters/PageSize'
        - $ref: '#/components/parameters/Query'
        - $ref: '#/components/parameters/Sort'
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/PagedPlanList' } } } }
    post:
      summary: Create subscription plan
      operationId: createPlan
      tags: [Subscriptions]
      security: [{ bearerAuth: [] }]
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/SubscriptionPlanCreate' } } }
      responses:
        '201': { description: Created, content: { application/json: { schema: { $ref: '#/components/schemas/SubscriptionPlan' } } } }

  /plans/{id}:
    get:
      summary: Get plan by id
      operationId: getPlanById
      tags: [Subscriptions]
      security: [{ bearerAuth: [] }]
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/SubscriptionPlan' } } } }
        '404': { $ref: '#/components/responses/NotFound' }
    put:
      summary: Update plan
      operationId: updatePlan
      tags: [Subscriptions]
      security: [{ bearerAuth: [] }]
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/SubscriptionPlanUpdate' } } }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/SubscriptionPlan' } } } }
    delete:
      summary: Delete plan
      operationId: deletePlan
      tags: [Subscriptions]
      security: [{ bearerAuth: [] }]
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '204': { description: No Content }

  /subscriptions:
    get:
      summary: List subscriptions for tenant
      operationId: listSubscriptions
      tags: [Subscriptions]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - $ref: '#/components/parameters/Page'
        - $ref: '#/components/parameters/PageSize'
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/PagedSubscriptionList' } } } }
    post:
      summary: Create subscription for tenant
      operationId: createSubscription
      tags: [Subscriptions]
      security: [{ bearerAuth: [] }]
      parameters: [ { $ref: '#/components/parameters/TenantHeader' } ]
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/SubscriptionCreate' } } }
      responses:
        '201': { description: Created, content: { application/json: { schema: { $ref: '#/components/schemas/Subscription' } } } }

  /subscriptions/{id}:
    get:
      summary: Get subscription by id
      operationId: getSubscriptionById
      tags: [Subscriptions]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/Subscription' } } } }
        '404': { $ref: '#/components/responses/NotFound' }
    delete:
      summary: Cancel subscription
      operationId: cancelSubscription
      tags: [Subscriptions]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '204': { description: No Content }

  # -------------- FILES --------------
  /files/upload:
    post:
      summary: Upload a file
      operationId: uploadFile
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
              properties: { file: { type: string, format: binary } }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/FileObject' } } } }

  /files/{objectName}:
    delete:
      summary: Delete a file
      operationId: deleteFile
      tags: [Files]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: objectName
          in: path
          required: true
          schema: { type: string }
      responses:
        '204': { description: No Content }
  # -------------- AUDIT --------------
  /audit/change-log:
    get:
      summary: Query audit logs
      operationId: queryAuditLogs
      tags: [Audit]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - in: query
          name: from
          schema: { type: string, format: date-time }
        - in: query
          name: to
          schema: { type: string, format: date-time }
        - in: query
          name: table
          schema: { type: string }
        - in: query
          name: operation
          schema: { type: string, enum: [INSERT, UPDATE, DELETE] }
        - in: query
          name: userId
          schema: { type: string }
        - $ref: '#/components/parameters/Page'
        - $ref: '#/components/parameters/PageSize'
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/PagedAuditList' } } } }

components:
  securitySchemes:
    bearerAuth:
      type: http
      scheme: bearer
      bearerFormat: JWT
    oauth2:
      type: oauth2
      description: 'Authorization Code (PKCE) flow for WebApp users.'
      flows:
        authorizationCode:
          authorizationUrl: https://auth.sbsaas.example.com/oauth2/authorize
          tokenUrl: https://auth.sbsaas.example.com/oauth2/token
          scopes:
            openid: OpenID Connect scope
            profile: Basic profile information
    apiKey:
      type: apiKey
      in: header
      name: X-Api-Key
  parameters:
    TenantHeader:
      in: header
      name: X-Tenant-Id
      required: true
      description: Tenant identifier (UUID)
      schema: { type: string, format: uuid }
    Page:
      in: query
      name: page
      schema: { type: integer, minimum: 1, default: 1 }
    PageSize:
      in: query
      name: pageSize
      schema: { type: integer, minimum: 1, maximum: 200, default: 20 }
    Query:
      in: query
      name: q
      schema: { type: string }
    Sort:
      in: query
      name: sort
      description: Comma-separated fields, prefix with '-' for desc. e.g. `-createdUtc,email`
      schema: { type: string }
  headers:
    XRateLimitLimit: { schema: { type: integer }, description: Limit }
    XRateLimitRemaining: { schema: { type: integer }, description: Kalan }
    XRateLimitReset: { schema: { type: integer }, description: Epoch seconds }
    Deprecation:
      description: 'RFC 8594. Kaynağın kullanımdan kaldırıldığını belirtir. Değer, kullanımdan kaldırılma tarihi olabilir.'
      schema:
        type: string
    Sunset:
      description: 'RFC 8594. Kaynağın tamamen kaldırılacağı tarih ve saat.'
      schema:
        type: string
        format: date-time
  responses:
    Unauthorized:
      description: Unauthorized
      content: { application/json: { schema: { $ref: '#/components/schemas/ProblemDetails' } } }
    NotFound:
      description: Resource not found
      content: { application/json: { schema: { $ref: '#/components/schemas/ProblemDetails' } } }
  schemas:
    ProblemDetails:
      type: object
      properties:
        type: { type: string }
        title: { type: string }
        status: { type: integer }
        detail: { type: string }
        traceId: { type: string }

    # -------- USERS --------
    User:
      type: object
      properties:
        id: { type: string, format: uuid }
        email: { type: string, format: email }
        displayName: { type: string }
        tenantId: { type: string, format: uuid }
        roles:
          type: array
          items: { type: string }
        createdUtc: { type: string, format: date-time }
    UserCreate:
      type: object
      required: [email, password]
      properties:
        email: { type: string, format: email }
        password: { type: string, format: password }
        displayName: { type: string }
    UserUpdate:
      type: object
      properties:
        displayName: { type: string }
        password: { type: string, format: password }
    RoleAssignment:
      type: object
      properties:
        roles:
          type: array
          items: { type: string }
    PagedUserList:
      type: object
      properties:
        items:
          type: array
          items: { $ref: '#/components/schemas/User' }
        page: { type: integer }
        pageSize: { type: integer }
        total: { type: integer }

    # -------- TENANTS --------
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
    TenantUpdate:
      type: object
      properties:
        name: { type: string }
        timeZone: { type: string }
    PagedTenantList:
      type: object
      properties:
        items:
          type: array
          items: { $ref: '#/components/schemas/Tenant' }
        page: { type: integer }
        pageSize: { type: integer }
        total: { type: integer }

    # -------- SUBSCRIPTIONS --------
    SubscriptionPlan:
      type: object
      properties:
        id: { type: string, format: uuid }
        code: { type: string, example: PRO }
        name: { type: string }
        price: { type: number, format: decimal }
        currency: { type: string, example: TRY }
        isActive: { type: boolean }
    SubscriptionPlanCreate:
      type: object
      required: [code, name, price]
      properties:
        code: { type: string }
        name: { type: string }
        price: { type: number, format: decimal }
        currency: { type: string, default: TRY }
        isActive: { type: boolean, default: true }
    SubscriptionPlanUpdate:
      type: object
      properties:
        name: { type: string }
        price: { type: number, format: decimal }
        currency: { type: string }
        isActive: { type: boolean }

    PagedPlanList:
      type: object
      properties:
        items:
          type: array
          items: { $ref: '#/components/schemas/SubscriptionPlan' }
        page: { type: integer }
        pageSize: { type: integer }
        total: { type: integer }

    Subscription:
      type: object
      properties:
        id: { type: string, format: uuid }
        tenantId: { type: string, format: uuid }
        planId: { type: string, format: uuid }
        startUtc: { type: string, format: date-time }
        endUtc: { type: string, format: date-time, nullable: true }
        autoRenew: { type: boolean }
    SubscriptionCreate:
      type: object
      required: [planId]
      properties:
        planId: { type: string, format: uuid }
        autoRenew: { type: boolean, default: true }
    PagedSubscriptionList:
      type: object
      properties:
        items:
          type: array
          items: { $ref: '#/components/schemas/Subscription' }
        page: { type: integer }
        pageSize: { type: integer }
        total: { type: integer }

    # -------- FILES --------
    FileObject:
      type: object
      properties:
        objectName: { type: string }
        bucket: { type: string }

    # -------- AUDIT --------
    AuditLog:
      type: object
      properties:
        id: { type: integer, format: int64 }
        tenantId: { type: string, format: uuid }
        tableName: { type: string }
        keyValues: { type: string, description: "JSON representation of the primary key(s)" }
        oldValues: { type: string, description: "JSON representation of the old values", nullable: true }
        newValues: { type: string, description: "JSON representation of the new values", nullable: true }
        operation: { type: string, enum: [INSERT, UPDATE, DELETE] }
        userId: { type: string, nullable: true }
        utcDate: { type: string, format: date-time }
    PagedAuditList:
      type: object
      properties:
        items:
          type: array
          items: { $ref: '#/components/schemas/AuditLog' }
        page: { type: integer }
        pageSize: { type: integer }
        total: { type: integer }

    # -------- AUTH --------
    LoginRequest:
      type: object
      required: [email, password]
      properties:
        email: { type: string, format: email }
        password: { type: string, format: password }
    RefreshTokenRequest:
      type: object
      required: [refreshToken]
      properties:
        refreshToken: { type: string }
    AuthTokens:
      type: object
      properties:
        accessToken: { type: string }
        refreshToken: { type: string }
        expiresIn: { type: integer }
    UserProfile:
      type: object
      properties:
        id: { type: string, format: uuid }
        email: { type: string }
        displayName: { type: string }
        roles: { type: array, items: { type: string } }
        tenantId: { type: string, format: uuid }
```

---

# 2) Hata Kodu Sözlüğü (Öneri)

- **400** – ValidationError (model hata detayları: alan, mesaj, kod)
- **401** – Unauthorized (token yok/geçersiz)
- **403** – Forbidden (rol/claim yetersiz)
- **404** – NotFound (kayıt yok)
- **409** – Conflict (benzersiz alan çakışması; örn. plan code)
- **422** – UnprocessableEntity (iş kuralı ihlali)
- **429** – Too Many Requests (rate limit)

> Şemada `ProblemDetails` kullanımıyla uyumlu.

---

# 3) Prism Mock – Örnek Dinamik Yanıtlar

`contracts/prism.yml` içinde `dynamic: true` açık olduğunda Prism şemaya göre örnekler üretir. Özel örnekler için `x-examples` ekleyebilirsin:
OpenAPI 3.1 standardına göre, örnekler `responses` -> `200` -> `content` -> `application/json` altına `examples` anahtar kelimesi ile eklenmelidir. Bu, hem dokümantasyon araçları hem de Prism tarafından doğru yorumlanmasını sağlar.

```yaml
paths:
  /users:
    get:
      # ... summary, tags, parameters etc.
      responses:
        '200':
          description: OK
          content:
            application/json:
              schema: { $ref: '#/components/schemas/PagedUserList' }
              examples:
                success:
                  summary: "Başarılı bir kullanıcı listesi yanıtı"
                  value:
                    items:
                      - id: "d290f1ee-6c54-4b01-90e6-d701748f0851" # UUID formatında
                        email: "admin@example.com"
                        displayName: "Admin"
                        tenantId: "11111111-1111-1111-1111-111111111111"
                        roles: ["Admin"]
                        createdUtc: "2025-01-01T00:00:00Z"
                    page: 1
                    pageSize: 20
                    total: 1
```

Çalıştırma:

```bash
prism mock contracts/openapi.yaml --port 4010
# Test
curl -H 'Authorization: Bearer mock' -H 'X-Tenant-Id: 11111111-1111-1111-1111-111111111111' http://127.0.0.1:4010/users
```

---

# 4) Postman Koleksiyonu Üretimi

```bash
npx openapi-to-postmanv2 -s contracts/openapi.yaml -o contracts/postman/SBSaaS.postman_collection.json -p -O folderStrategy=Tags
```

---

# 5) Notlar

- **Tenant İzolasyonu**: `X-Tenant-Id` başlığı, `/users`, `/subscriptions` gibi tenant'a özgü tüm kaynaklar için zorunludur. `/tenants` ve `/plans` gibi yönetici seviyesi veya `/auth` gibi global uçlar bu başlığı gerektirmez.
- **Sayfalama (Pagination)**: Listeleme yanıtları standart bir `PagedList` yapısı kullanır: `{ items: T[], page, pageSize, total }`.
- **Sıralama (Sorting)**: `?sort=-createdUtc,email` gibi virgülle ayrılmış ve `-` ön ekiyle azalan sıralamayı destekleyen çoklu alan sıralaması mevcuttur.
- **Arama (Searching)**: `?q=` parametresi ile basit metin araması sağlanmıştır. Gelecekte daha karmaşık filtreleme için `?filter=` standardı (örn: OData) eklenebilir.
- **Operasyon Kimlikleri**: Tüm operasyonlar için `operationId` alanı, SDK üretimi ve istemci entegrasyonunu kolaylaştırmak amacıyla `verbResource` (örn: `listUsers`, `createTenant`) formatında standartlaştırılmıştır.
- **Hata Yönetimi**: Hata yanıtları, RFC 7807 ile uyumlu `ProblemDetails` şemasını kullanarak standart bir formatta sunulur. Bu, istemcilerin hataları tutarlı bir şekilde işlemesini sağlar.
- **Veritabanı Performansı**: Audit sorguları, A2 iş paketi kapsamında `(tenant_id, utc_date)` gibi bileşik indekslerle desteklenerek yüksek performansta çalışacak şekilde planlanmıştır.

---

# 6) Kabul Kriterleri (G1 güncellemesi)

- **Kapsam**: `Users`, `Subscriptions` (Planlar dahil) ve `Audit` yolları, ilgili DTO'lar (`User`, `SubscriptionPlan`, `AuditLog` vb.) ve operasyonlar (`listUsers`, `createPlan` vb.) şemaya eksiksiz eklenmiştir.
- **Standartlar**:
  - Tüm yeni operasyonlar için `operationId` standardı (`verbResource` formatında) uygulanmıştır.
  - Sayfalama (`PagedList`), arama (`q`) ve sıralama (`sort`) için ortak parametreler ve şemalar tutarlı bir şekilde kullanılmıştır.
- **Mock Testi**: Prism ile `/users`, `/plans`, `/subscriptions` ve `/audit/change-log` uçlarından başarılı mock yanıtları alınabilmektedir.
- **Koleksiyon**: Postman koleksiyonu, OpenAPI şemasından `folderStrategy=Tags` kullanılarak yeniden üretilmiş ve istekler etiketlere göre klasörlenmiştir.
- **CI Doğrulaması**: Güncellenen `openapi.yaml` dosyası, CI pipeline'daki lint (Spectral) ve mock smoke test adımlarını başarıyla geçmektedir.

```
```
