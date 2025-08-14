Bu belge **F2 – Usage Metering & Billing Sync** iş paketinin uçtan uca uygulanabilir kılavuzudur. Amaç: özellik/kota (F1) için **kullanım olaylarını** toplamak, **dönemsel** (gün/ay/fatura dönemi) agregasyon üretmek, **overage** faturalarını idempotent şekilde oluşturmak ve (opsiyonel) **Stripe metered billing** ile senkron çalışmaktır.

---

# 0) DoD – Definition of Done
- `usage_event` → `usage_daily` → `usage_period` boru hattı otomatik çalışıyor (Worker/pgagent/Quartz).
- API/servisler F1’deki sayaçları **event** olarak yayınlıyor; double count yok (idempotency key).
- Dönem bitiminde **overage** satırı oluşturuluyor veya Stripe metered usage’a raporlanıyor.
- Mutabakat raporu: `usage_period` ↔ faturalanan miktar eşleşiyor; sapmalar raporlanıyor.
- Gözlemlenebilirlik metrikleri ve alarm eşikleri tanımlı.

---

# 1) Şema (billing + metering)
**Şema:** `billing` altında veya ayrı `metering` şeması (öneri: `metering`). Aşağıda `metering` kullanıldı.

```sql
-- Ham kullanım olayları (append-only)
CREATE SCHEMA IF NOT EXISTS metering;

CREATE TABLE IF NOT EXISTS metering.usage_event (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  key text NOT NULL,            -- örn: api_calls, storage_mb, seats
  quantity numeric(18,6) NOT NULL DEFAULT 1,
  occurred_at timestamptz NOT NULL,
  source text NOT NULL,         -- api, job, import, webhook
  idempotency_key text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, key, idempotency_key)
);

-- Günlük agregasyon (UTC gün)
CREATE TABLE IF NOT EXISTS metering.usage_daily (
  tenant_id uuid NOT NULL,
  key text NOT NULL,
  day date NOT NULL,
  quantity numeric(18,6) NOT NULL DEFAULT 0,
  PRIMARY KEY (tenant_id, key, day)
);

-- Fatura dönemi agregasyonu
CREATE TABLE IF NOT EXISTS metering.usage_period (
  tenant_id uuid NOT NULL,
  key text NOT NULL,
  period_start date NOT NULL,
  period_end date NOT NULL,
  quantity numeric(18,6) NOT NULL DEFAULT 0,
  closed boolean NOT NULL DEFAULT false,
  PRIMARY KEY (tenant_id, key, period_start, period_end)
);

-- Stripe metered senkron durumu
CREATE TABLE IF NOT EXISTS metering.external_sync_state (
  provider text NOT NULL,    -- 'stripe'
  tenant_id uuid NOT NULL,
  key text NOT NULL,
  external_ref text NOT NULL, -- usage_record_id / subscription_item_id
  last_synced_at timestamptz,
  UNIQUE(provider, tenant_id, key)
);
```

> Not: Overage (F1 `billing.overage`) zaten var; `usage_period` kapanınca üretilecek.

---

# 2) Uçtan Uca Akış
1. **Event Yayını** – API uçları veya servisler, önemli aksiyonlarda `RecordUsageAsync(tenantId, key, qty, idemKey)` çağırır.
2. **Günlük Agregasyon** – job, `usage_event` → `usage_daily` upsert.
3. **Dönem Agregasyon** – job, `usage_daily`’den aktif aboneliğin fatura dönemine göre `usage_period` toplar.
4. **Kapanış** – dönem bitiminde plan limitleriyle karşılaştır, aşım varsa `billing.overage` oluştur **veya** Stripe’a usage gönder.
5. **Faturalama** – E1/E2 ile fatura/ödemeye yansır (stripe invoice/iyzico manuel).

---

# 3) Application Servisleri
**`IMeteringService`**
```csharp
public interface IMeteringService
{
    Task RecordUsageAsync(Guid tenantId, string key, decimal quantity, string source, string idempotencyKey, DateTimeOffset? occurredAt = null, CancellationToken ct = default);
}
```
**Uygulama** (özet)
```csharp
public class MeteringService : IMeteringService
{
    private readonly SbsDbContext _db; private readonly ILogger<MeteringService> _log;
    public MeteringService(SbsDbContext db, ILogger<MeteringService> log){_db=db; _log=log;}

    public async Task RecordUsageAsync(Guid tenantId, string key, decimal qty, string source, string idem, DateTimeOffset? at, CancellationToken ct)
    {
        var e = new UsageEvent{ Id=Guid.NewGuid(), TenantId=tenantId, Key=key, Quantity=qty, Source=source, IdempotencyKey=idem, OccurredAt=(at??DateTimeOffset.UtcNow).UtcDateTime };
        _db.Add(e);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { _log.LogInformation("Duplicate usage idem={Idem}", idem); }
    }
}
```

**Idempotency**: `(tenant_id, key, idempotency_key)` unique.

---

# 4) Zamanlama & İşler
## 4.1 Günlük Agregasyon Job
Pseudo-SQL:
```sql
INSERT INTO metering.usage_daily (tenant_id, key, day, quantity)
SELECT tenant_id, key, (occurred_at AT TIME ZONE 'UTC')::date AS day, SUM(quantity)
FROM metering.usage_event e
WHERE e.occurred_at >= now() - interval '2 days' -- son 48 saat pencere, idempotent
GROUP BY tenant_id, key, (occurred_at AT TIME ZONE 'UTC')::date
ON CONFLICT (tenant_id, key, day)
DO UPDATE SET quantity = metering.usage_daily.quantity + EXCLUDED.quantity;
```
Zamanlayıcı seçenekleri: **pgAgent**, **Quartz.NET** (Worker), ya da **Hangfire**.

## 4.2 Dönem Agregasyon Job
Abonelik dönemini (start + cycle) E1 tablosundan al:
```sql
-- örnek: son kapanmamış dönemleri topla
INSERT INTO metering.usage_period (tenant_id, key, period_start, period_end, quantity)
SELECT d.tenant_id, d.key, s.period_start, s.period_end, SUM(d.quantity)
FROM metering.usage_daily d
JOIN LATERAL (
  -- aktif aboneliğin fatura dönemleri
  SELECT start_date as period_start,
         (CASE WHEN plan.billing_cycle='monthly' THEN (date_trunc('month', start_date) + interval '1 month' - interval '1 day')
               WHEN plan.billing_cycle='yearly' THEN (date_trunc('year', start_date) + interval '1 year' - interval '1 day') END)::date as period_end
  FROM billing.subscription s
  JOIN billing.plan plan ON plan.id = s.plan_id
  WHERE s.tenant_id = d.tenant_id AND s.status='active'
  LIMIT 1
) s ON true
WHERE d.day BETWEEN s.period_start AND s.period_end
GROUP BY d.tenant_id, d.key, s.period_start, s.period_end
ON CONFLICT (tenant_id, key, period_start, period_end)
DO UPDATE SET quantity = EXCLUDED.quantity;
```
> Pratikte dönem hesabını uygulama kodunda netleştirip, dönemi kapatma anında hesaplamak daha güvenlidir.

## 4.3 Dönem Kapanışı Job
- `usage_period.closed=false` olan ve `period_end < today` olan kayıtlar için:
  1) Plan limitini/overage fiyatını F1/E1’den oku.
  2) `excess = max(0, quantity - limit)`.
  3) **Overage** gerekiyorsa `billing.overage` satırı oluştur (idempotent anahtar: `tenantId+key+period_start`).
  4) `closed=true` olarak işaretle.

---

# 5) Stripe Metered Billing Köprüsü (Opsiyonel)
- E1’de plan ürünlerinin Stripe tarafında **metered** `price` objeleriyle eşleştirildiğini varsayalım.
- `external_sync_state` tablosunda `subscription_item_id` eşlemesini saklayın.
- Job, kapanan dönem miktarını **UsageRecord** olarak gönderir:
```csharp
var service = new Stripe.UsageRecordService();
await service.CreateAsync(subscriptionItemId, new UsageRecordCreateOptions{
  Quantity = (long)Math.Round(quantity),
  Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
  Action = "set" // veya "increment" stratejine göre
});
```
- Stripe tarafı faturayı keser → E2 webhook’ları ile **invoice.paid** geldiğinde iç tablolar güncellenir.

**Idempotency**: Aynı dönem için aynı `subscription_item_id`’ye sadece bir kez `set`/`increment`. `external_sync_state.last_synced_at` ile korun.

---

# 6) API Uçları
- `POST /api/v1/metering/events` – internal/privileged kullanım; batch kabul eder.
- `GET /api/v1/metering/usage?granularity=daily&periodStart=...&periodEnd=...&key=...` – raporlama.
- `POST /api/v1/metering/close-period` – admin/manual tetikleme (opsiyonel).

OpenAPI (G1) için şema örnekleri ekleyin; `X-Tenant-Id` zorunlu.

---

# 7) Gözlemlenebilirlik & Alarmlar
- Metrikler: `usage_events_ingested_total{key}`, `usage_daily_compactions_total`, `periods_closed_total`, `overage_created_total`.
- Uyarılar:
  - Event ingest hatası artarsa (5xx oranı) alarm.
  - Dönem kapanması gecikirse (SLA > X saat) alarm.
  - Stripe sync hatası tekrar sayısı > N ise alarm.

---

# 8) Veri Kalitesi & Backfill
- **Geç Gelen Olaylar**: `usage_daily` upsert’i 48–72 saat geriye bakacak şekilde çalıştır.
- **Backfill**: CSV/NDJSON import aracı (`/metering/events` batch) ile eski veriyi yükle.
- **Silme/Düzeltme**: Append-only `usage_event` üzerinde düzeltmeyi `reverse` (negatif quantity) ile yap.

---

# 9) Güvenlik & PII
- Olaylarda kullanıcı e-postası gibi PII kullanmayın; `tenant_id` + anonim `subject_id` (ops.).
- İç uçları `[Authorize(Policy=AdminOnly)]` korumasına alın; rate limit.
- Event idempotency key’i tahmin edilemez (GUID/nonce) olsun.

---

# 10) Test Planı
- **Idempotency**: Aynı `idempotency_key` ile tekrar kayıt → tek satır.
- **Dönem Toplamı**: günlük → dönem toplamı doğru mu?
- **Overage**: limit < kullanım → overage satırı oluştu mu, idempotent mi?
- **Stripe Sync**: usage record gönderildi mi, tekrarında engellendi mi?
- **Backfill**: geçmiş gün event’leri eklenince agregasyon güncelleniyor mu?

---

# 11) Sonraki Paket
- **F3 – Quota Enforcement@Edge**: API Gateway/Reverse Proxy seviyesinde (NGINX/Envoy/YARP) tenant başına edge rate limiting ve merkezi kuyruklama; metering ile çift yönlü senkron.

