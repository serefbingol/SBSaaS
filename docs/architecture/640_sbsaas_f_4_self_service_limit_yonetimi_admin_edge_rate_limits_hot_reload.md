Bu belge  **F4 – Self‑Service Limit Yönetimi (Admin)**  iş paketinin uçtan uca kılavuzudur. Hedef: Admin kullanıcılarının  **plan/tenant**  bazlı  **edge rate limit**  (RPS, burst, pencere) ayarlarını UI’dan yönetebilmesi; gateway (YARP/NGINX/Envoy) tarafında  **hot‑reload**  ile anında etkili olması; audit ve güvenli konfig akışı.

----------

## 0) DoD – Definition of Done

-   Plan ve tenant seviyesinde  `rps`,  `burst`,  `windowSeconds`  ayarları CRUD tamam.
-   “Effective limit” çözümleme sırası:  **Tenant override → Plan default → System default**.
-   Gateway’de limit değişikliği  **anında**  (≤5 sn) devreye giriyor (pub/sub veya management API).
-   Tüm değişiklikler  `audit.change_log`  ve  `config.limit_history`’de kayıtlı.
-   UI doğrulama, rollback (son bilinen iyi konfig’e dön) ve onay akışı (ops.) mevcut.

----------

## 1) Şema

**Schema:**  `config`

```
CREATE SCHEMA IF NOT EXISTS config;

-- Plan default edge limits
CREATE TABLE IF NOT EXISTS config.plan_limit (
  plan_id uuid PRIMARY KEY,
  rps int NOT NULL CHECK (rps > 0),
  burst int NOT NULL CHECK (burst >= rps),
  window_seconds int NOT NULL CHECK (window_seconds BETWEEN 1 AND 3600),
  updated_at timestamptz NOT NULL DEFAULT now(),
  updated_by uuid NULL
);

-- Tenant overrides (opsiyonel)
CREATE TABLE IF NOT EXISTS config.tenant_limit (
  tenant_id uuid PRIMARY KEY,
  rps int NOT NULL CHECK (rps > 0),
  burst int NOT NULL CHECK (burst >= rps),
  window_seconds int NOT NULL CHECK (window_seconds BETWEEN 1 AND 3600),
  reason text NULL,
  expires_at timestamptz NULL,
  updated_at timestamptz NOT NULL DEFAULT now(),
  updated_by uuid NULL
);

-- Versiyon/geri alma için tarihçeleme
CREATE TABLE IF NOT EXISTS config.limit_history (
  id bigserial PRIMARY KEY,
  scope text NOT NULL,          -- 'plan' | 'tenant'
  scope_id uuid NOT NULL,       -- plan_id veya tenant_id
  payload jsonb NOT NULL,
  version int NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  created_by uuid NULL
);

-- System default (tek satır)
CREATE TABLE IF NOT EXISTS config.system_default (
  id bool PRIMARY KEY DEFAULT true,
  rps int NOT NULL DEFAULT 10,
  burst int NOT NULL DEFAULT 100,
  window_seconds int NOT NULL DEFAULT 1,
  updated_at timestamptz NOT NULL DEFAULT now(),
  updated_by uuid NULL
);
```

**Görünüm (effective limit)**

```
CREATE OR REPLACE VIEW config.v_effective_limits AS
SELECT t.tenant_id,
       COALESCE(t.rps, p.rps, s.rps) AS rps,
       COALESCE(t.burst, p.burst, s.burst) AS burst,
       COALESCE(t.window_seconds, p.window_seconds, s.window_seconds) AS window_seconds
FROM (SELECT tenant_id, rps, burst, window_seconds FROM config.tenant_limit WHERE expires_at IS NULL OR expires_at > now()) t
RIGHT JOIN (
  SELECT s.tenant_id, pl.rps, pl.burst, pl.window_seconds
  FROM billing.subscription s
  JOIN config.plan_limit pl ON pl.plan_id = s.plan_id
  WHERE s.status = 'active'
) p ON p.tenant_id = t.tenant_id
CROSS JOIN config.system_default s;
```

----------

## 2) API (Admin)

Route tabanı:  `/api/v1/admin/limits`  (RBAC:  `Admin,Owner`)

```
GET    /plans                → Plan limit listesi
GET    /plans/{planId}       → Plan limit detay
PUT    /plans/{planId}       → Plan limit güncelle (upsert)

GET    /tenants/{tenantId}   → Tenant override detay
PUT    /tenants/{tenantId}   → Tenant override upsert
DELETE /tenants/{tenantId}   → Tenant override kaldır

POST   /rollback             → { scope: 'plan'|'tenant', scopeId, version } geri alma
GET    /effective/{tenantId} → Çözülmüş limit (rps, burst, windowSeconds)
```

**DTO’lar**

```
public record LimitUpsertDto(int Rps, int Burst, int WindowSeconds, string? Reason = null, DateTimeOffset? ExpiresAt = null);
public record LimitViewDto(Guid Id, int Rps, int Burst, int WindowSeconds, DateTimeOffset UpdatedAt, Guid? UpdatedBy);
public record EffectiveLimitDto(Guid TenantId, int Rps, int Burst, int WindowSeconds);
public record RollbackRequest(string Scope, Guid ScopeId, int Version);
```

**Kurallar**

-   `burst ≥ rps`,  `1 ≤ windowSeconds ≤ 3600`.
-   Tenant override’da  **son kullanma**  (`expires_at`) desteklenir.
-   Upsert sonrası  `limit_history`’ye  **otomatik versiyon**  kaydı.

----------

## 3) Admin UI (Razor)

**Navigasyon**

```
/Admin/Limits
  / Plans     → Plan limitleri listesi + düzenleme
  / Tenants   → Tenant arama + override düzenleme
```

**Ekranlar**

-   **Plan Limits**: tablo (Plan, RPS, Burst, Window) + Edit modal; kaydet → API  `PUT /plans/{planId}`.
-   **Tenant Overrides**: arama (tenant adı/email), detay kartı; override gir/bitiş tarihi ekle; kaldır butonu.
-   **Effective Preview**: Tenant seçince  `GET /effective/{tenantId}`  ile göstergeler.
-   **History/Rollback**: son 10 versiyon listesi; seç →  `POST /rollback`.

**Validasyon & UX**

-   Inline doğrulama (min/max), helper metinleri (ör. “Burst, kısa patlamalara tolerans sağlar”).
-   Override girerken  **reason**  zorunlu kılınabilir.

----------

## 4) Gateway Hot‑Reload

İki seçenekten biri (veya ikisi) uygulanır:

### 4.1 Redis Pub/Sub (Önerilen)

-   Admin API upsert/delete sonrası  `PUBLISH rl:update {"tenantId": "..."}`.
-   Gateway  `SUBSCRIBE rl:update`  dinler → ilgili anahtarı  **cache’den düşürür**  (veya yeni parametreleri Redis hash’ten okur).
-   YARP  `ITenantKeyResolver`  +  `IRedisBucket`  parametreleri  **run‑time**  çekerek yeni  `rps/burst/window`  ile çalışır.

**Redis anahtarları**

```
rl:limit:tenant:{id} → hash { rps, burst, window }
rl:limit:plan:{id}   → hash { rps, burst, window }
```

Gateway çözüm sırası: tenant hash → plan hash → system default.

### 4.2 Management API (YARP)


-   Gateway’de korumalı bir endpoint:  `POST /_admin/reload-limits`.
-   Admin API, upsert sonrası gateway’e çağrı yapar; gateway DB/Redis’ten konfig’i yeniden çeker.
-   Güvenlik: mTLS veya imzalı JWT; IP allowlist.

**YARP örnek kod (özet)**

```
app.MapPost("/_admin/reload-limits", [Authorize(Policy="GatewayAdmin")] async (ILimitCache cache) => { await cache.ReloadAsync(); return Results.NoContent(); });
```

----------

## 5) YARP Token Bucket – Parametrik Kullanım


`RedisTokenBucketLimiter`  içinde  **partition**  tarafından  `rps/burst/window`  dinamik gelir.

```
var cfg = await _limitStore.GetEffectiveAsync(tenant);
return RateLimitPartition.Get("tenant:"+tenant, key => new TokenBucketRateLimiter(new(){
  TokenLimit = cfg.Burst,
  TokensPerPeriod = cfg.Rps,
  ReplenishmentPeriod = TimeSpan.FromSeconds(cfg.WindowSeconds),
  AutoReplenishment = true,
  QueueLimit = 0
}));
```

> Not: .NET dahili limiter state’i process içidir; dağıtık tutarlılık için Redis‑Lua ile atomik tüketim kullanın (F3). Bu bölüm parametrelerin  **dinamikleşmesi**  içindi.

----------

## 6) Güvenlik & Audit
-   Admin uçları  `[Authorize(Roles="Admin,Owner")]`  + opsiyonel  **ikincil onay**  (two‑man rule) için  `PendingChange`  tablosu eklenebilir.
-   Tüm değişiklikler  `audit.change_log`  ve  `config.limit_history`’ye düşer (eski → yeni JSON payload).
-   **Rate limit yönetimi**  endpoint’leri ve gateway management sadece  **internal network + mTLS**.

----------

## 7) Observability
-   Olaylar:  `LimitUpdated`,  `OverrideCreated`,  `OverrideExpired`,  `GatewayReloaded`.
-   Metrikler:  `edge_limit_rps{tenant}`,  `edge_limit_burst{tenant}`,  `edge_limit_changes_total`.
-   Alarm: “override sayısı > N” veya “çok sık limit değişimi” anomali uyarısı.

----------

## 8) Test Planı

-   Plan limitini değiştir → gateway’de  **≤5 sn**  içinde  `X-RateLimit-*`  değerleri yeni limitleri yansıtıyor mu?
-   Tenant override ekle →  **effective**  önizleme ve gerçek trafik testi.
-   `expires_at`  dolduğunda otomatik temizleniyor mu? (scheduler)
-   Rollback → önceki versiyona dönüş ve gateway hot‑reload doğrulaması.
-   Güvenlik: yetkisiz kullanıcı 403; gateway management endpoint’e erişim reddi.
-   Dayanıklılık: Redis down → fail‑open/closed davranışı beklenen mi?

----------

## 9) Sonraki Paket

-   **G1 – Sözleşme Güncellemeleri**: OpenAPI’da  `X-RateLimit-*`  başlıkları, admin limit uçlarının şeması; sözleşmede plan/tenant limit maddeleri.
-   **Ops**: Limit konfiglerini yedekleme ve migration script’leri; staging → prod terfi akışı için “config promotion” aracı.