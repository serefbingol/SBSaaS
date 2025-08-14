Bu belge **E1 – Faturalama & Planlama Şeması** iş paketinin uçtan uca uygulanabilir kılavuzudur. Amaç, SaaS platformunda çok kiracılı (multi-tenant) abonelik yönetimi, plan/özellik tanımı, fiyatlandırma, faturalama ve ödeme akışının temel veritabanı şeması ile API uçlarının tasarlanmasıdır.

---

# 0) DoD – Definition of Done
- Veritabanında plan, özellik, abonelik, fatura ve ödeme tabloları tanımlandı.
- Tenant başına aktif abonelik bilgisi tutuluyor.
- Plan/özellik yönetimi API uçları çalışıyor.
- Faturalama ve ödeme kayıtları oluşturulabiliyor.
- OpenAPI şemasında tüm uçlar belgelenmiş.
- Testler: Plan oluşturma, abonelik başlatma, fatura kesme, ödeme alma senaryoları.

---

# 1) Veritabanı Şeması
**Schema:** `billing`

## Tablolar
```sql
CREATE TABLE billing.plan (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    price DECIMAL(10,2) NOT NULL,
    currency CHAR(3) NOT NULL,
    billing_cycle TEXT NOT NULL, -- monthly, yearly
    created_at TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE billing.feature (
    id UUID PRIMARY KEY,
    plan_id UUID NOT NULL REFERENCES billing.plan(id),
    key TEXT NOT NULL,
    value TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE billing.subscription (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    plan_id UUID NOT NULL REFERENCES billing.plan(id),
    start_date DATE NOT NULL,
    end_date DATE,
    status TEXT NOT NULL, -- active, cancelled, expired
    created_at TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE billing.invoice (
    id UUID PRIMARY KEY,
    subscription_id UUID NOT NULL REFERENCES billing.subscription(id),
    issue_date DATE NOT NULL,
    due_date DATE NOT NULL,
    amount DECIMAL(10,2) NOT NULL,
    currency CHAR(3) NOT NULL,
    status TEXT NOT NULL, -- unpaid, paid, overdue
    created_at TIMESTAMP NOT NULL DEFAULT now()
);

CREATE TABLE billing.payment (
    id UUID PRIMARY KEY,
    invoice_id UUID NOT NULL REFERENCES billing.invoice(id),
    payment_date DATE NOT NULL,
    amount DECIMAL(10,2) NOT NULL,
    currency CHAR(3) NOT NULL,
    method TEXT NOT NULL, -- credit_card, wire_transfer, etc.
    transaction_id TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT now()
);
```

---

# 2) API Uçları (Örnek)

## Plan Yönetimi
- `POST /api/v1/billing/plans` → Plan oluştur.
- `GET /api/v1/billing/plans` → Plan listesi.
- `PUT /api/v1/billing/plans/{id}` → Plan güncelle.
- `DELETE /api/v1/billing/plans/{id}` → Plan sil.

## Abonelik Yönetimi
- `POST /api/v1/billing/subscriptions` → Yeni abonelik başlat.
- `GET /api/v1/billing/subscriptions` → Tenant abonelik listesi.
- `PATCH /api/v1/billing/subscriptions/{id}/cancel` → Aboneliği iptal et.

## Faturalama
- `POST /api/v1/billing/invoices` → Manuel fatura oluştur.
- `GET /api/v1/billing/invoices` → Fatura listesi.
- `GET /api/v1/billing/invoices/{id}` → Fatura detayı.

## Ödemeler
- `POST /api/v1/billing/payments` → Ödeme kaydı ekle.
- `GET /api/v1/billing/payments` → Ödeme listesi.

---

# 3) İş Akışı
1. **Plan Tanımı**: Admin panelden planlar ve özellikler tanımlanır.
2. **Abonelik Başlatma**: Tenant, plan seçerek abonelik başlatır.
3. **Fatura Kesme**: Abonelik başlangıcında veya dönemsel olarak fatura oluşturulur.
4. **Ödeme Alma**: Fatura karşılığı ödeme kaydı eklenir; ödeme servisi (Stripe, iyzico vb.) entegre edilir.
5. **Durum Güncelleme**: Ödeme geldiğinde fatura "paid", abonelik "active" olarak güncellenir.

---

# 4) Entegrasyon Notları
- Ödeme servisleri için **webhook listener** eklenmeli (başarılı/başarısız ödeme).
- Fiyatlandırma para birimleri ve vergiler (KDV vb.) konfigürasyona tabi olmalı.
- Plan özellikleri feature flag sistemi ile entegre edilebilir.

---

# 5) Test Senaryoları
- Plan oluşturma ve güncelleme.
- Abonelik başlatma ve iptal etme.
- Fatura oluşturma ve ödeme alma.
- Ödeme başarısız olduğunda abonelik durumu.

---

# 6) Sonraki Paket
- **E2 – Ödeme Sağlayıcı Entegrasyonu**: Stripe/iyzico gibi servislerle ödeme alma ve webhook işleme.

