# Kapsamlı SaaS Uygulama Geliştirme Planı

Bu doküman, sağlanan mimari belgeler ve kod parçaları temel alınarak hazırlanan, `SBSaaS` projesini hayata geçirmek için gereken adımları içeren fazlandırılmış bir plandır.

---

### Faz 0: Temel Altyapı ve Geliştirme Ortamının Kurulması

Bu faz, kod yazmaya başlamadan önce projenin temelini ve çalışacağı ortamı hazırlamaya odaklanır.

1.  **Proje ve Çözüm Yapısını Oluşturma:**
    *   `b_sbsaas_cozum_yapisi.md` dokümanında belirtilen Clean Architecture yapısına uygun olarak Visual Studio veya `dotnet new` komutları ile aşağıdaki projeleri oluşturun:
        *   `SBSaaS.Domain`
        *   `SBSaaS.Application`
        *   `SBSaaS.Infrastructure`
        *   `SBSaaS.API`
        *   `SBSaaS.WebApp`
        *   `SBSaaS.Worker` (Bu, `251_ProjectWorker...` dokümanı için gereklidir)
    *   Projeler arasındaki referansları (`API` -> `Application`, `Infrastructure` -> `Application` vb.) doğru şekilde ayarlayın.

2.  **Docker Ortamını Ayağa Kaldırma:**
    *   `221_Docker_Compose_Yapilandirma_Plani.md` dokümanını temel alarak projenizin kök dizininde bir `docker` klasörü oluşturun.
    *   İçine `docker-compose.yml` dosyasını oluşturun. Bu dosya şu servisleri içermelidir:
        *   `sbsaas_postgres` (Timescale, PostGIS eklentileri ile)
        *   `sbsaas_rabbitmq` (Worker servisinin olayları dinlemesi için)
        *   `sbsaas_storage1`, `..._storage2`, vb. (MinIO servisleri)
        *   `sbsaas_clamav`
    *   Hassas veriler için bir `.env.example` dosyası hazırlayın ve asıl `.env` dosyasını `.gitignore`'a ekleyin.
    *   `docker compose up -d` komutu ile tüm altyapı servislerinin (veritabanı, MinIO, RabbitMQ, ClamAV) çalıştığından emin olun.

---

### Faz 1: Veri Katmanı ve Çoklu Kiracılık (Multi-Tenancy)

Artık altyapı ayakta olduğuna göre, uygulamanın veritabanı ile konuşmasını ve verileri kiracılara göre izole etmesini sağlayacağız.

1.  **Entity ve DbContext'i Oluşturma:**
    *   `SBSaaS.Domain` projesinde temel `Entity`'lerinizi (örn: `Project`, `User`, `Tenant`) oluşturun.
    *   `SBSaaS.Infrastructure` projesinde `SbsDbContext`'i oluşturun.
    *   `SbsDbContextTests.cs` dosyasındaki mantığı referans alarak `SaveChangesAsync` metodunu override edin. Bu, yeni kayıtlara otomatik `TenantId` ekleyecek ve kiracılar arası yetkisiz erişimi engelleyecektir.

2.  **Çoklu Kiracılık (Multi-Tenancy) Context'ini Uygulama:**
    *   `SBSaaS.Application` katmanında `ITenantContext` arayüzünü tanımlayın. Bu arayüz, mevcut isteğin hangi kiracıya ait olduğunu (`TenantId`) tutacaktır.
    *   `SBSaaS.API` projesinde, gelen isteklerdeki `X-Tenant-Id` gibi bir header'ı veya JWT token içindeki bir claim'i okuyup `ITenantContext`'i dolduran bir `Middleware` yazın.

3.  **Veritabanı Migration'ı ve İlk API Ucu:**
    *   `dotnet ef migrations add InitialCreate` komutu ile ilk veritabanı şemanızı oluşturun.
    *   `dotnet ef database update` ile bu şemayı Docker'daki PostgreSQL veritabanına uygulayın.
    *   `SBSaaS.API` projesinde, veritabanı bağlantısını test etmek için basit bir `/health` veya `/version` endpoint'i oluşturun.

---

### Faz 2: Kimlik Doğrulama ve Yetkilendirme

Uygulamamız artık veri tabanına erişebiliyor. Şimdi kullanıcıların sisteme giriş yapmasını ve güvenliği sağlayalım.

1.  **API için JWT Yapılandırması:**
    *   `SBSaaS.API`'nin `Program.cs` dosyasında, `.env` dosyasındaki `JWT_SECRET`, `JWT_ISSUER` gibi değerleri kullanarak JWT Bearer Authentication'ı yapılandırın.
    *   Temel kullanıcı kaydı ve token üreten bir `AuthenticationController` oluşturun.

2.  **WebApp için OAuth ve Cookie Entegrasyonu:**
    *   `320_sbsaas_c_2_oauth_login...` dokümanını takip ederek `SBSaaS.WebApp` projesine `Microsoft.AspNetCore.Authentication.Google` ve `MicrosoftAccount` paketlerini ekleyin.
    *   `Program.cs` içinde Cookie Authentication ve dış sağlayıcıları (Google/Microsoft) yapılandırın.
    *   `AccountController`'ı ve `Login.cshtml` view'ını oluşturarak "Sign in with Google/Microsoft" akışını tamamlayın.

---

### Faz 3: Ana Özellik: Asenkron Dosya Yükleme ve Tarama

Sistemin en karmaşık ve kritik akışlarından birini, yani dosya yönetimini uçtan uca inşa edelim.

1.  **MinIO Entegrasyonu ve Presigned URL:**
    *   `150_sbsaas_a_5_min_io_entegrasyonu...` dokümanındaki gibi `IObjectSigner` ve `IUploadPolicy` arayüzlerini `Application` katmanında tanımlayın.
    *   Bu arayüzleri `Infrastructure` katmanında MinIO SDK'sını kullanarak implemente edin.
    *   `SBSaaS.API` projesinde, istemcilerin dosya yüklemek için geçici ve güvenli URL'ler almasını sağlayan `FilesPresignController`'ı oluşturun.

2.  **Worker Servisi ve Olay Dinleme:**
    *   MinIO yönetim arayüzünden veya `mc` CLI ile, dosya yükleme olaylarını (`s3:ObjectCreated:*`) Faz 0'da kurduğumuz RabbitMQ'ya gönderecek şekilde bir bildirim (notification) yapılandırın.
    *   `SBSaaS.Worker` projesini bir "Background Service" olarak yapılandırın. Bu servis, RabbitMQ'daki ilgili kuyruğu dinleyecek.

3.  **Virüs Tarama ve İş Mantığı:**
    *   `251_ProjectWorker_minio_antivirus_kontrol.md`'deki akışı takip edin:
        *   Worker, RabbitMQ'dan gelen "yeni dosya" mesajını alır.
        *   Dosyanın metadata'sını (`tenant-id` vb.) MinIO'dan okur.
        *   Dosyayı MinIO'dan stream olarak çeker ve doğrudan Docker ağındaki `sbsaas_clamav` servisine tarama için gönderir.
        *   Sonuca göre (temiz/virüslü) dosyayı siler veya denetim (audit) kaydı oluşturur.

---

### Faz 4: Gelişmiş SaaS Özellikleri

Çekirdek ürün çalışır durumda. Şimdi para kazanma ve hizmet kalitesini yönetme özelliklerini ekleyelim.

1.  **Kullanım Ölçümleme (Metering):**
    *   `620_sbsaas_f_2_usage_metering...` dokümanındaki `metering` şemasını (`usage_event`, `usage_daily` vb.) veritabanına bir EF migration ile ekleyin.
    *   `IMeteringService`'i oluşturun. Dosya yükleme gibi önemli olaylarda bu servisi çağırarak `usage_event` tablosuna kayıt atın.
    *   `SBSaaS.Worker` içinde, bu olayları periyodik olarak toplayıp `usage_daily` ve `usage_period` tablolarını güncelleyen zamanlanmış bir görev (örn: Quartz.NET veya Hangfire ile) oluşturun.

2.  **Rate Limiting Yönetimi:**
    *   `640_sbsaas_f_4_self_service_limit_yonetimi...` dokümanındaki `config` şemasını veritabanına ekleyin.
    *   `SBSaaS.API` içinde, adminlerin plan ve kiracı bazlı limitleri yönetebileceği API uç noktalarını oluşturun.

---

### Faz 5: Otomasyon ve Dağıtım (CI/CD)

Artık manuel işleri bırakıp, test ve dağıtım süreçlerini otomatikleştirme zamanı.

1.  **GitHub Actions Pipeline'larını Kurma:**
    *   `410_sbsaas_d_1_ci_cd_pipeline...` dokümanındaki `ci.yml` (PR doğrulaması için) ve `release.yml` (main branch'e merge sonrası için) dosyalarını projenizin `.github/workflows` klasörüne ekleyin.
    *   `ci.yml`'daki adımların (build, test, EF migration check, lint) çalıştığından emin olun.
    *   `release.yml`'daki Docker imajı build edip GHCR'a (GitHub Container Registry) push etme adımlarını test edin.
    *   Gerekli `secrets` (örn: `POSTGRES_CONNECTION`) değişkenlerini GitHub repository ayarlarınıza ekleyin.