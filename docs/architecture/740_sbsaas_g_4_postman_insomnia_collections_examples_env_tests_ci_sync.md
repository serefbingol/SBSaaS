Bu belge **G4 – Postman/Insomnia Collections & Examples** iş paketinin uçtan uca kılavuzudur. Hedef: OpenAPI (G1) kaynağından **Postman** ve **Insomnia** koleksiyonlarını otomatik üretmek, ortamlara (Prod/Staging/Mock) göre **env değişkenleri** tanımlamak, **pre-request**/**test** script’leriyle `X-Tenant-Id`, `X-Correlation-Id`, auth ve **rate-limit** davranışlarını standartlaştırmak; CI ile senkron tutmak ve örnek istek/yanıtları sürdürmektir.

---

# 0) DoD – Definition of Done

- `docs/postman` ve `docs/insomnia` altında koleksiyon ve environment dosyaları mevcut.
- **OpenAPI → Postman/Insomnia** otomatik üretim adımı CI’da çalışıyor.
- Env değişkenleri: `baseUrl`, `tenantId`, `accessToken` vb. tanımlı; pre-request script’leri ile `X-Correlation-Id` oluşturuluyor.
- Test script’leri: **status**, **schema**, **rate-limit header** kontrolü, **Retry-After** backoff örneği.
- Mock (Prism), Staging, Prod için ayrı environment’lar; örnekler (examples) doldurulmuş.

---

# 1) Dizin Yapısı

```
docs/
  postman/
    collection.json          # otomatik üretilen (kaynak)
    collection.enriched.json # script’ler/örneklerle zenginleştirilmiş
    env.mock.json
    env.staging.json
    env.prod.json
  insomnia/
    sbsaas-insomnia.yaml     # Designer export (API Spec + Requests)
    env.mock.yaml
    env.staging.yaml
    env.prod.yaml
```

---

# 2) OpenAPI → Postman Dönüşümü

**Araçlar:**

- Postman **API** paneli (import from URL/file)
- CLI: `openapi-to-postman` (Postman destekli)

**Komut**

```bash
npx openapi-to-postmanv2 -s contracts/openapi.yaml -o docs/postman/collection.json -p -O folderStrategy=Tags
```

- `-p`: örnekleri (examples) oluşturur.
- `folderStrategy=Tags`: OAS tag’lerine göre klasör düzeni.

**CI Entegrasyonu** (özet, G3 Docs job’ına eklenir)

```yaml
- name: Generate Postman Collection
  run: npx openapi-to-postmanv2 -s contracts/openapi.yaml -o docs/postman/collection.json -p -O folderStrategy=Tags
```

---

# 3) Postman Environment’ları

\`\` (örnek)

```json
{
  "id": "uuid-mock",
  "name": "SBSaaS Mock",
  "values": [
    { "key": "baseUrl", "value": "http://localhost:4010", "type": "text" },
    { "key": "tenantId", "value": "00000000-0000-0000-0000-000000000001", "type": "text" },
    { "key": "accessToken", "value": "{{pm.environment.jwt}}", "type": "text" }
  ]
}
```

**Staging/Prod**: `baseUrl` ve varsayılan `tenantId` güncellenir.

---

# 4) Postman Pre-request & Test Script’leri

## 4.1 Pre-request – Header Enjeksiyonu

```js
// X-Tenant-Id, X-Correlation-Id, Authorization
pm.request.headers.upsert({ key: 'X-Tenant-Id', value: pm.environment.get('tenantId') })
pm.request.headers.upsert({ key: 'X-Correlation-Id', value: pm.variables.replaceIn('{{$guid}}') })
const token = pm.environment.get('accessToken')
if (token) pm.request.headers.upsert({ key: 'Authorization', value: `Bearer ${token}` })
```

## 4.2 Test – Status, Schema, Rate-Limit

```js
pm.test('HTTP 2xx', () => pm.response.code >= 200 && pm.response.code < 300)

// Rate-limit headers (F3/F4)
pm.test('RateLimit headers present', () => {
  pm.expect(pm.response.headers.has('X-RateLimit-Limit')).to.be.true
  pm.expect(pm.response.headers.has('X-RateLimit-Remaining')).to.be.true
})

// Retry-After örnek backoff (yalnız 429/503)
if ([429,503].includes(pm.response.code)) {
  const ra = pm.response.headers.get('Retry-After')
  const reset = pm.response.headers.get('X-RateLimit-Reset')
  const wait = ra ? Number(ra) : Math.max(0, (Number(reset)||0) - Math.floor(Date.now()/1000))
  postman.setNextRequest(pm.info.requestName) // aynı isteği tekrar çalıştır
  pm.environment.set('backoff_ms', (wait + Math.random()) * 1000)
}
```

> **Not:** `setNextRequest` backoff’u uygulamak için `pre-request`te `setTimeout` kullanılamaz; bunun yerine Collection Runner’ın iteration delay özelliği ya da Newman ile delay parametresi kullanılmalı.

## 4.3 Schema Validasyonu

- Postman’da OAS tabanlı **Schema** doğrulaması **API** sekmesiyle yapılabilir; script tabanlı hızlı kontrol için minimal JSON şeması ekleyin.

---

# 5) Örnekler (Examples) ve Varyantlar

- Koleksiyon öğelerinde **Examples**: başarılı (200), yetkisiz (401), rate-limited (429) yanıtları.
- `Start Checkout` için prod ve mock ayrı örnek gövdeleri.
- i18n: `Accept-Language: tr-TR` ve `en-US` varyant example’ları.

---

# 6) Newman ile CI Testleri

**Amaç:** PR’larda temel duman testleri.

**Komutlar**

```bash
npx newman run docs/postman/collection.enriched.json \
  -e docs/postman/env.mock.json \
  --delay-request 100 \
  --reporters cli,junit --reporter-junit-export newman-results.xml
```

**CI Adımı**

```yaml
- name: Newman Smoke Tests (Mock)
  run: |
    npx @stoplight/prism-cli mock contracts/openapi.yaml --port 4010 &
    sleep 2
    npx newman run docs/postman/collection.enriched.json -e docs/postman/env.mock.json --delay-request 50 --reporters cli
```

---

# 7) Insomnia – Designer & Collection

**Yöntem 1 (Önerilen):** **Insomnia Designer**’da doğrudan `openapi.yaml`’ı açıp Collections üretin.

- Env değişkenleri: `base_url`, `tenant_id`, `access_token`.
- **Request Template**: `X-Tenant-Id: {{ _.tenant_id }}`; `Authorization: Bearer {{ _.access_token }}`; `X-Correlation-Id: {% uuid %}`.

**Yöntem 2:** Postman koleksiyonunu Insomnia’ya **import** edin.

**Export**: `docs/insomnia/sbsaas-insomnia.yaml`

---

# 8) Güvenlik & PII Notları

- Koleksiyonlarda **secret** içermeyin; env dosyalarını örnek/template olarak paylaşın (`*.example.json`).
- Webhook örneklerinde PII maskeleyin; imza başlıkları için **placeholder** kullanın.

---

# 9) Sürümleme & Yayınlama

- Koleksiyon sürümü = OAS sürümü (`v1.2.0`).
- **Public Postman Workspace** (ops.): `SBSaaS` alanı; CI, yeni koleksiyonu **Postman API** ile günceller.
- Değişiklikler **Changelog** (G3) ile senkron.

**Postman API Sync (ops.)**

```yaml
- name: Publish to Postman Workspace
  env:
    POSTMAN_API_KEY: ${{ secrets.POSTMAN_API_KEY }}
  run: |
    curl -X POST https://api.getpostman.com/collections \
      -H "X-Api-Key: $POSTMAN_API_KEY" \
      -H "Content-Type: application/json" \
      -d @docs/postman/collection.enriched.json
```

---

# 10) Test Planı

- Import: Postman ve Insomnia’da koleksiyonlar hatasız içe aktarılıyor mu?
- Env switch: Mock/Staging/Prod geçişlerinde baseUrl ve header’lar doğru mu?
- Auth: `accessToken` boşsa 401; doluysa 200 senaryosu.
- Rate-limit: 429 senaryosu ve header kontrolleri yeşil mi? (Mock ile simülasyon)
- Schema: Örnek yanıtlar OAS şemasıyla uyumlu mu?

---

# 11) Sonraki Paket

- **G5 – Internal Runbooks & Playbooks**: Operasyon kılavuzları (deploy, incident, rollback, rate-limit tuning), sık sorulanlar ve sorun giderme (troubleshooting) dokümanları.

