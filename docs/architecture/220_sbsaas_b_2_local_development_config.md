Bu belge **B2 – Local Development Config** iş paketini içerir. Amaç, geliştiricilerin yerel ortamda SBSaaS API’yi kolayca çalıştırabilmesi, yapılandırabilmesi ve test edebilmesidir.

---

# 0) DoD – Definition of Done

- `appsettings.*.json` profilleri oluşturuldu.
- Hassas bilgiler `dotnet user-secrets` ile yönetiliyor.
- EF Core CLI ve migration komutları çalışıyor.
- Hot reload ve watch senaryoları aktif.
- Yerel Docker Compose (B1) ile entegre çalışıyor.

---

# 1) appsettings Profilleri

`src/SBSaaS.API/appsettings.Development.json` örneği:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=sbsaasdb;Username=postgres;Password=postgres"
  },
  "Minio": {
    "Endpoint": "localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "UseSSL": false,
    "DefaultBucket": "sbsaas"
  },
  "Jwt": {
    "Issuer": "https://sbsaas.local",
    "Audience": "sbsaas-clients",
    "SigningKey": "ThisIsMyDevelopmentSigningKeyAndItMustBeChangedForProduction!12345"
  }
}
```

> Üretim ortamında bu bilgileri dosyada tutmayın; `user-secrets` veya çevresel değişkenlerle yönetin.

---

# 2) dotnet user-secrets

```bash
cd src/SBSaaS.API
# init
 dotnet user-secrets init
# set secrets
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=sbsaasdb;Username=postgres;Password=postgres"
dotnet user-secrets set "Minio:AccessKey" "minioadmin"
dotnet user-secrets set "Minio:SecretKey" "minioadmin"
dotnet user-secrets set "Jwt:SigningKey" "SuperSecretSigningKey_ChangeMe_!@#123"
```

> **Not:** `user-secrets` sadece geliştirme ortamında çalışır. Deployment sırasında kullanılmaz.

---

# 3) EF Core CLI & Migrations

**Global tool kurulumu:**

```bash
dotnet tool install --global dotnet-ef
```

**Migration ekleme:**

```bash
cd src/SBSaaS.Infrastructure
dotnet ef migrations add Init --startup-project ../SBSaaS.API --context SbsDbContext
```

**Veritabanı güncelleme:**

```bash
dotnet ef database update --startup-project ../SBSaaS.API --context SbsDbContext
```

---

# 4) Hot Reload & Watch

```bash
cd src/SBSaaS.API
dotnet watch run
```

- Kod değişikliklerinde API otomatik yeniden başlar.
- `launchSettings.json` ile farklı profiller (`Development`, `Docker`) yönetilebilir.

---

# 5) Yerel Docker Compose ile Çalışma

B1 paketindeki compose dosyası ile DB ve MinIO ayağa kalkar.

```bash
docker compose up -d postgres minio
# API'yi local dotnet run ile başlatabilirsiniz
dotnet run --project src/SBSaaS.API
```

---

# 6) Test & Doğrulama

- `/health` endpoint’ine istek → 200 OK.
- Presigned URL endpoint’i ile dosya yükleme testi.
- EF migration sonrası tenant seed verileri kontrolü.

---

# 7) Sonraki Paket

- **C1 – Razor i18n**: WebApp tarafında çok dilli UI ve formatlama desteği.
