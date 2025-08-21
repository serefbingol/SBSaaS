Bu belge **B1 – Docker Compose** iş paketinin uçtan uca kurulum ve yapılandırma kılavuzudur. Amaç: PostgreSQL + MinIO + API konteynerları ile entegre, yerel geliştirme ortamı kurmak; MinIO bucket/init script’leri, CORS ayarları ve healthcheck'leri hazır etmek.

---

# 0) DoD – Definition of Done
- `docker-compose.yml` ile PostgreSQL, MinIO ve API konteynerları ayağa kalkıyor.
- PostgreSQL için **init script** ile tenant tabanlı temel veriler yükleniyor.
- MinIO için **bucket init** ve **CORS** ayarları otomatik yapılıyor.
- API konteynerı PostgreSQL ve MinIO’ya bağlanabiliyor.
- Healthcheck'ler tüm servislerde çalışıyor.
- Geliştirici onboarding adımları dökümante edildi.

---

# 1) Dizim ve Dosyalar
```
project-root/
 ├─ docker-compose.yml
 ├─ docker/
 │   ├─ postgres-init.sql
 │   ├─ minio-init.sh
 │   └─ api.Dockerfile
```

---

# 2) docker-compose.yml
```yaml
version: '3.9'

services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_USER: sbsaas
      POSTGRES_PASSWORD: sbsaas123
      POSTGRES_DB: sbsaasdb
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./docker/postgres-init.sql:/docker-entrypoint-initdb.d/init.sql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U sbsaas"]
      interval: 10s
      timeout: 5s
      retries: 5

  minio:
    image: minio/minio:latest
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    ports:
      - "9000:9000"
      - "9001:9001"
    volumes:
      - minio_data:/data
      - ./docker/minio-init.sh:/docker-entrypoint-init.d/minio-init.sh
    healthcheck:
      test: ["CMD", "mc", "ready", "local"]
      interval: 30s
      timeout: 20s
      retries: 3

  clamav:
    image: clamav/clamav:latest
    ports:
      - "3310:3310" # ClamAV daemon port
    volumes:
      - clamav_data:/var/lib/clamav
    healthcheck:
      test: ["CMD", "clamdscan", "--ping"]
      interval: 30s
      timeout: 10s
      retries: 5

  freshclam:
    image: clamav/clamav:latest
    command: freshclam --checks=12 --daemon-address=clamav --daemon-port=3310
    volumes:
      - clamav_data:/var/lib/clamav
    depends_on:
      clamav:
        condition: service_healthy
    restart: on-failure

  api:
    build:
      context: .
      dockerfile: ./docker/api.Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Default: "Host=postgres;Database=sbsaasdb;Username=sbsaas;Password=sbsaas123"
      Minio__Endpoint: "minio:9000"
      Minio__AccessKey: "minioadmin"
      Minio__SecretKey: "minioadmin"
      Minio__UseSSL: "false"
    ports:
      - "5000:5000"
    depends_on:
      postgres:
        condition: service_healthy
      minio:
        condition: service_healthy

  worker:
    build:
      context: .
      dockerfile: ./docker/worker.Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Default: "Host=postgres;Database=sbsaasdb;Username=sbsaas;Password=sbsaas123"
      Minio__Endpoint: "minio:9000"
      Minio__AccessKey: "minioadmin"
      Minio__SecretKey: "minioadmin"
      Minio__Bucket: "sbsaas" # Ensure this matches minio-init.sh
      ClamAV__Host: "clamav"
      ClamAV__Port: "3310"
    depends_on:
      postgres:
        condition: service_healthy
      minio:
        condition: service_healthy
      clamav:
        condition: service_healthy
    restart: on-failure

volumes:
  postgres_data:
  minio_data:
  clamav_data: # New volume for ClamAV definitions
```

---

# 3) PostgreSQL Init Script – docker/postgres-init.sql
```sql
CREATE SCHEMA IF NOT EXISTS audit;
CREATE SCHEMA IF NOT EXISTS subscription;
-- Seed system tenant
INSERT INTO tenants (id, name) VALUES ('00000000-0000-0000-0000-000000000001', 'System Tenant');
```

---

# 4) MinIO Init Script – docker/minio-init.sh
```bash
#!/bin/sh
set -e

mc alias set local http://minio:9000 minioadmin minioadmin
mc mb local/sbsaas || true
mc anonymous set none local/sbsaas
mc admin config set local/ api cors="[{'AllowedOrigin':['*'],'AllowedMethod':['GET','PUT','POST'],'AllowedHeader':['*'],'ExposeHeader':['ETag'],'MaxAgeSeconds':3000}]"
```

> **Not**: Üretimde `AllowedOrigin` değerini sınırlayın.

---

# 5) API Dockerfile – docker/api.Dockerfile
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/SBSaaS.API/SBSaaS.API.csproj
RUN dotnet publish src/SBSaaS.API/SBSaaS.API.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SBSaaS.API.dll"]
```

---

# 6) Geliştirici Onboarding Adımları
```bash
# İlk kez çalıştırma
chmod +x docker/minio-init.sh
docker compose up -d --build

# Logları izleme
docker compose logs -f api

# MinIO Console erişimi
http://localhost:9001  # user: minioadmin / pass: minioadmin

# API erişimi
http://localhost:5000/swagger
```

---

# 7) Test Senaryoları
- API konteynerı PostgreSQL’e bağlanabiliyor mu?
- API konteynerı MinIO’ya presigned URL ile dosya yükleyebiliyor mu?
- MinIO CORS ayarları doğru çalışıyor mu?
- PostgreSQL init script’i ile system tenant oluştu mu?

---

# 8) Sonraki Paket
- **B2 – Local Development Config**: appsettings profilleri, user-secrets yönetimi, hot reload desteği.