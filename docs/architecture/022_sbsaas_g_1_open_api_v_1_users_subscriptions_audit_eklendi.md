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
      tags: [Auth]
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/LoginRequest' }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/AuthTokens' } } } }
        '401': { $ref: '#/components/responses/Unauthorized' }

  /me:
    get:
      summary: Returns current user profile
      tags: [Auth]
      security: [{ bearerAuth: [] }]
      parameters: [ { $ref: '#/components/parameters/TenantHeader' } ]
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/UserProfile' } } } }
        '401': { $ref: '#/components/responses/Unauthorized' }

  /tenants:
    get:
      summary: List tenants (admin)
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
      tags: [Tenants]
      security: [{ bearerAuth: [] }]
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/TenantCreate' } } }
      responses:
        '201': { description: Created, content: { application/json: { schema: { $ref: '#/components/schemas/Tenant' } } } }

  # ---------------- USERS ----------------
  /users:
    get:
      summary: List users in tenant
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
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/User' } } } }
        '404': { $ref: '#/components/responses/NotFound' }
    put:
      summary: Update user
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string }
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/UserUpdate' } } }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/User' } } } }
        '404': { $ref: '#/components/responses/NotFound' }
    delete:
      summary: Delete user
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string }
      responses:
        '204': { description: No Content }
        '404': { $ref: '#/components/responses/NotFound' }

  /users/{id}/roles:
    put:
      summary: Replace user roles
      tags: [Users]
      security: [{ bearerAuth: [] }]
      parameters:
        - $ref: '#/components/parameters/TenantHeader'
        - name: id
          in: path
          required: true
          schema: { type: string }
      requestBody:
        required: true
        content: { application/json: { schema: { $ref: '#/components/schemas/RoleAssignment' } } }
      responses:
        '204': { description: No Content }

  # -------------- SUBSCRIPTIONS & PLANS --------------
  /plans:
    get:
      summary: List subscription plans
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

  # -------------- AUDIT --------------
  /audit/change-log:
    get:
      summary: Query audit logs
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
        id: { type: string }
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

    # -------- AUTH --------
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
    UserProfile:
      type: object
      properties:
        id: { type: string }
        email: { type: string }
        displayName: { type: string }
        roles: { type: array, items: { type: string } }
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

```yaml
paths:
  /users:
    get:
      x-examples:
        success:
          value:
            items:
              - id: "u_1"
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
npx openapi-to-postmanv2 -s contracts/openapi.yaml -o contracts/postman/SBSaaS.postman_collection.json -p
```

---

# 5) Notlar

- `X-Tenant-Id` tüm tenant’a bağlı uçlarda zorunlu.
- Paging yanıtları: `{ items: T[], page, pageSize, total }`.
- Sıralama: `?sort=-createdUtc,email` biçiminde çoklu alan desteği.
- Arama: `?q=` basit arama parametresi (geliştirilebilir filtreleme için `?filter=` şeması ileride eklenebilir).
- Audit sorguları performans için tarih + tenant indeksleri ile desteklenecek (A2 kapsamı).

---

# 6) Kabul Kriterleri (G1 güncellemesi)

- Users, Subscriptions(Plans), Audit yolları ve **DTO şemaları** eklendi.
- Prism ile `/users`, `/plans`, `/subscriptions`, `/audit/change-log` mock yanıtları alınabiliyor.
- Postman koleksiyonu güncelleniyor ve çalışır.

```
```
