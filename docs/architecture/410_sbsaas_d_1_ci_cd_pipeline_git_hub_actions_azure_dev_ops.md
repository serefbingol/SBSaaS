Bu belge **D1 – CI/CD Pipeline** iş paketini kapsar. Hedef:

- **PR doğrulamaları** (build/test/lint/OpenAPI lint/migration check),
- **main** için versiyonlama ve **Docker image build & push**,
- Ortam tabanlı **deploy job** iskeletleri (Staging/Prod),
- Hem **GitHub Actions** hem **Azure DevOps** (dev.azure) YAML’ları.

> Önce GitHub Actions kullan; Azure DevOps’a taşımak istersen ikinci YAML hazır.

---

# 0) DoD – Definition of Done

- PR pipeline: `dotnet build`, `dotnet test`, **EF migration check**, **OpenAPI lint** (Spectral), **API mock smoke** (Prism), **Docker build** dry-run; artefactlar yüklenir.
- main/release pipeline: **version tag** üretimi (MinVer), **Docker image** build & push (GHCR/ACR), **db migrations** komutu, **Staging deploy** onaylı.
- Secrets & env değişkenleri dokümante edildi.

---

# 1) Repo Düzeni (özet)

```
contracts/openapi.yaml
.github/workflows/ci.yml
.github/workflows/release.yml
src/SBSaaS.API/
src/SBSaaS.Infrastructure/
```

---

# 2) GitHub Actions – CI (PR) (`.github/workflows/ci.yml`)

```yaml
name: CI
on:
  pull_request:
    branches: [ main, develop ]
  push:
    branches: [ feature/** ]

jobs:
  build-test:
    runs-on: ubuntu-latest
    env:
      DOTNET_CLI_TELEMETRY_OPTOUT: 1
      DOTNET_NOLOGO: 1
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build -c Release --no-restore

      - name: Test
        run: dotnet test -c Release --no-build --logger "trx;LogFileName=test.trx"

      - name: Upload Test Results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/TestResults/*.trx'

  openapi-lint:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Install Spectral
        run: npm i -g @stoplight/spectral-cli
      - name: Lint OpenAPI
        run: spectral lint contracts/openapi.yaml

  ef-migration-check:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:17
        env:
          POSTGRES_PASSWORD: postgres
          POSTGRES_DB: sbsaas_ci
        ports: [ '5432:5432' ]
        options: >-
          --health-cmd="pg_isready -U postgres" --health-interval=10s --health-timeout=5s --health-retries=5
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '9.0.x' }
      - name: Add EF Tools
        run: dotnet tool install --global dotnet-ef
      - name: Apply Migrations
        env:
          ConnectionStrings__Postgres: Host=localhost;Port=5432;Database=sbsaas_ci;Username=postgres;Password=postgres
        run: |
          dotnet ef database update --project src/SBSaaS.Infrastructure --startup-project src/SBSaaS.API

  prism-mock-smoke:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Install Prism
        run: npm i -g @stoplight/prism-cli
      - name: Start mock (bg)
        run: nohup prism mock contracts/openapi.yaml --port 4010 &
      - name: Smoke test
        run: |
          sleep 2
          curl -f http://127.0.0.1:4010/users || exit 1

  docker-build:
    runs-on: ubuntu-latest
    needs: [ build-test, openapi-lint, ef-migration-check, prism-mock-smoke ]
    steps:
      - uses: actions/checkout@v4
      - name: Build API image
        run: docker build -t sbsaas-api:ci ./src/SBSaaS.API
```

---

# 3) GitHub Actions – Release (main) (`.github/workflows/release.yml`)

```yaml
name: Release
on:
  push:
    branches: [ main ]

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}/api

jobs:
  version:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.minver.outputs.version }}
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with: { dotnet-version: '9.0.x' }
      - name: Install MinVer
        run: dotnet tool install --global minver-cli
      - id: minver
        run: echo "version=$(minver -v e --tag-prefix v)" >> $GITHUB_OUTPUT

  build-push:
    runs-on: ubuntu-latest
    needs: version
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4
      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - name: Extract metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=raw,value=${{ needs.version.outputs.version }}
            type=raw,value=latest
      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          context: ./src/SBSaaS.API
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}

  migrate-db:
    runs-on: ubuntu-latest
    needs: build-push
    steps:
      - uses: azure/login@v2
        if: env.AZURE_CLIENT_ID
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      - name: Run EF migrations (container)
        env:
          ConnectionStrings__Postgres: ${{ secrets.POSTGRES_CONNECTION }}
        run: |
          docker run --rm \
            -e ConnectionStrings__Postgres=${{ secrets.POSTGRES_CONNECTION }} \
            ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ needs.version.outputs.version }} \
            dotnet ef database update --project /app/SBSaaS.Infrastructure --startup-project /app/SBSaaS.API

  deploy-staging:
    runs-on: ubuntu-latest
    needs: migrate-db
    environment: Staging
    steps:
      - name: Deploy (placeholder)
        run: echo "Deploy to Staging orchestrator here (AKS/ACI/AppService)"
```

**Notlar**

- Versiyonlama: **MinVer** semver’ı git tag’lerinden (örn. `v1.2.3`) türetir. Tag yoksa `0.1.0+{sha}` oluşur.
- Registry: `ghcr.io/<owner>/<repo>/api:version`. Azure ACR için login/action değerlerini değiştirin.

---

# 4) Azure DevOps – Multi-stage Pipeline (`azure-pipelines.yml`)

```yaml
trigger:
  branches: { include: [ main, develop ] }
pr:
  branches: { include: [ main, develop ] }

variables:
  buildConfiguration: 'Release'

stages:
- stage: CI
  jobs:
  - job: BuildTest
    pool: { vmImage: 'ubuntu-latest' }
    steps:
    - checkout: self
      fetchDepth: 0
    - task: UseDotNet@2
      inputs: { packageType: 'sdk', version: '9.x' }
    - script: dotnet restore
      displayName: Restore
    - script: dotnet build -c $(buildConfiguration) --no-restore
      displayName: Build
    - script: dotnet test -c $(buildConfiguration) --no-build --logger trx
      displayName: Test
    - script: |
        npm i -g @stoplight/spectral-cli
        spectral lint contracts/openapi.yaml
      displayName: OpenAPI Lint

- stage: Release
  dependsOn: CI
  condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
  jobs:
  - job: BuildPush
    pool: { vmImage: 'ubuntu-latest' }
    steps:
    - checkout: self
    - task: Docker@2
      inputs:
        command: buildAndPush
        repository: $(Build.Repository.Name)/api
        dockerfile: src/SBSaaS.API/Dockerfile
        containerRegistry: $(dockerServiceConnection)
        tags: |
          latest
          $(Build.BuildNumber)
```

> `dockerServiceConnection` için Azure Container Registry (ACR) Service Connection oluşturun.

---

# 5) EF Migration Check (Detay)

- PR’da geçici Postgres service ile `` çalıştırıyoruz.
- Alternatif:
  - `ef migrations script --idempotent` üretip artefact olarak saklayın,
  - üretimde `psql` ile uygulayın.

**Idempotent script üretimi (release job):**

```bash
dotnet ef migrations script --idempotent -o artifacts/migrations.sql \
  --project src/SBSaaS.Infrastructure --startup-project src/SBSaaS.API -c SbsDbContext
```

---

# 6) Secrets & Değişkenler

- **GitHub**: `Settings → Secrets and variables → Actions`
  - `POSTGRES_CONNECTION` (prod/staging),
  - `AZURE_*` (varsa deployment),
  - `REGISTRY_USERNAME/REGISTRY_PASSWORD` (eğer GHCR dışı kullanıyorsan),
  - OAuth client secrets (C2 için WebApp pipeline’ında gerekecek).
- **Azure DevOps**: Library/Variable Groups + Secret.

---

# 7) Artefact & Raporlama

- Test TRX dosyalarını artefact olarak yükledik.
- İsteğe bağlı: **Code Coverage** (`--collect:"XPlat Code Coverage"`) ekleyip `reportgenerator` ile HTML rapor üret.

---

# 8) Branch/Commit/Tag Politikası (öneri)

- Branch: `feature/*`, `bugfix/*`, `hotfix/*`, `release/*`.
- PR kuralları: 1 review, status checks required, conventional commits (opsiyonel).
- Release: `main`’e merge sonrası **tag** oluştur (`v1.0.0`).

---

# 9) Deploy İskeleleri (örnek)

- **AKS/Helm**: `kubectl set image` veya Helm chart (separate repo) – environment secrets ile `ConnectionStrings__Postgres`, `Minio__*`, `Jwt__*`.
- **App Service (Linux)**: `azure/webapps-deploy@v2` action; slot swap (Staging→Production).
- **Docker Swarm**: `docker stack deploy` komutları.

---

# 10) Hızlı Başlangıç

1. `contracts/openapi.yaml` mevcut ve `docker build` API için geçerli olmalı.
2. Repo’da `.github/workflows/ci.yml` ve `release.yml` dosyalarını oluştur.
3. GHCR için `Settings → Packages` izinlerini aç, workflow’u çalıştır.
4. `main`’e merge et → image push & (opsiyonel) deploy adımını tetikle.

---

# 11) Sonraki Paket

- **D2 – Logging & Monitoring**: Serilog + OpenTelemetry; lokalde Grafana/Tempo/Loki; prod’da APM/metrics/log pipeline’ı.

