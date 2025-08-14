Bu belge **G4 – Postman/Insomnia Collections & Examples** iş paketinin kılavuzudur. Hedef: SBSaaS OpenAPI sözleşmesinden otomatik olarak Postman ve Insomnia koleksiyonlarını üretmek, ortam değişkenleri ile yapılandırmak ve CI/CD sürecinde güncel tutmaktır.

---

# 0) DoD – Definition of Done
- OpenAPI şemasından otomatik olarak Postman ve Insomnia koleksiyonları üretiliyor.
- Koleksiyonlar tenant ID, auth token, base URL gibi ortam değişkenleri ile parametrik.
- Koleksiyonlar portal üzerinden indirilebilir veya Postman API/Insomnia Sync ile paylaşılabilir.
- Koleksiyonlarda CRUD uç noktaları, örnek istekler, mock/prod ortam bağlantıları mevcut.
- CI/CD pipeline koleksiyonları OAS ile senkronize ediyor.

---

# 1) Araçlar ve Üretim
- **openapi-to-postmanv2** CLI veya **postman/openapi-to-postman** npm paketi.
- **Insomnia Designer** ile OAS içe aktarma ve koleksiyon export.
- Tenant ve auth başlıkları için environment.json dosyaları.

---

# 2) Ortamlar
**Postman Environment Variables**:
- `baseUrl`: API URL’si (mock, staging, prod)
- `tenantId`: Aktif tenant GUID
- `authToken`: OAuth2/JWT token

**Insomnia Environment Variables**:
- `{{ baseUrl }}`, `{{ tenantId }}`, `{{ authToken }}`

---

# 3) CI/CD
- `npm run gen:postman` ve `npm run gen:insomnia` komutları ile koleksiyon export.
- CI pipeline’da OAS güncellemesi sonrası koleksiyonların yeniden üretilip repo’ya commit edilmesi.
- Portalda otomatik link güncellemesi.

---

# 4) Portal Entegrasyonu
- Developer Portal’da “Download Postman Collection” ve “Download Insomnia Collection” düğmeleri.
- Ortam dosyalarının ve import talimatlarının yer aldığı rehber.

---

# 5) Test Planı
- Koleksiyon import sonrası tüm isteklerin mock ve prod ortamında çalışması.
- Environment değişkenlerinin doğru şekilde değer alması.
- Auth flow’un koleksiyon içinden çalışabilir olması.

---

# 6) Sonraki Paket
- **G5 – Developer Analytics & Usage Dashboard**: Portal üzerinden geliştirici başına API kullanım istatistikleri, hata oranları, en çok kullanılan uçlar.

