# Faz 4: Gelişmiş SaaS Özellikleri - Detaylı İş Planı

Bu faz, projenin ticari değerini ve sürdürülebilirliğini artıracak iki ana iş paketi üzerine kuruludur: **Kullanım Ölçümleme (Metering)** ve **Rate Limiting (Kota) Yönetimi**.

---

## İş Paketi 4.1: Kullanım Ölçümleme (Metering)

**Amaç:** Sistemin ürettiği değeri (örn: depolama, API çağrısı) ölçülebilir olaylara dönüştürmek, bu olayları toplamak, periyodik olarak özetlemek ve faturalandırmaya hazır hale getirmek.

**Referans Doküman:** `620_sbsaas_f_2_usage_metering_billing_sync_sayac_toplama_donemsel_raporlama_overage_stripe_metered.md`

### Adım Adım Kırılım:

1.  **Veritabanı Şemasını Genişletme:**
    *   **Adım 4.1.1:** `SBSaaS.Domain` projesinde, `Entities` klasörü altına `metering` şemasına karşılık gelen şu entity sınıflarını oluşturun:
        *   `UsageEvent.cs`
        *   `UsageDaily.cs`
        *   `UsagePeriod.cs`
        *   `ExternalSyncState.cs` (Stripe entegrasyonu için)
    *   **Adım 4.1.2:** `SBSaaS.Infrastructure` projesindeki `SbsDbContext.cs` dosyasına bu yeni entity'ler için `DbSet`'ler ekleyin.
        ```csharp
        public DbSet<UsageEvent> UsageEvents { get; set; }
        public DbSet<UsageDaily> UsageDailies { get; set; }
        public DbSet<UsagePeriod> UsagePeriods { get; set; }
        ```
    *   **Adım 4.1.3:** `OnModelCreating` metodu içinde, `UsageEvent` için `(TenantId, Key, IdempotencyKey)` üzerine `UNIQUE` kısıtlaması gibi referans dokümandaki şema kurallarını Fluent API ile tanımlayın.
    *   **Adım 4.1.4:** Projenin kök dizininde `dotnet ef migrations add AddMeteringSchema -p src/SBSaaS.Infrastructure -s src/SBSaaS.API` komutu ile yeni bir veritabanı migrasyonu oluşturun.
    *   **Adım 4.1.5:** `dotnet ef database update` komutu ile değişiklikleri veritabanına uygulayın.

2.  **Metering Servisini Oluşturma:**
    *   **Adım 4.2.1:** `SBSaaS.Application` projesinde `Interfaces` klasörü altına `IMeteringService.cs` arayüzünü oluşturun ve `RecordUsageAsync` metodunu tanımlayın.
    *   **Adım 4.2.2:** `SBSaaS.Infrastructure` projesinde `Services` klasörü altına `MeteringService.cs` sınıfını oluşturun ve `IMeteringService` arayüzünü implemente edin.
    *   **Adım 4.2.3:** `MeteringService` implementasyonunda, `DbUpdateException`'ı yakalayarak mükerrer `IdempotencyKey` kayıt denemelerini loglayın. Bu bir hata değil, beklenen bir durumdur, bu yüzden exception'ı yutun.
    *   **Adım 4.2.4:** `SBSaaS.API` projesinin `Program.cs` dosyasında `builder.Services.AddScoped<IMeteringService, MeteringService>();` satırı ile servisi DI container'a kaydedin.

3.  **Kullanım Olaylarını Tetikleme (Dosya Yükleme Örneği):**
    *   **Adım 4.3.1:** `SBSaaS.Worker` projesindeki `FileScanConsumerWorker.cs` dosyasını açın.
    *   **Adım 4.3.2:** `UpdateFileScanResultAsync` metoduna `IMeteringService`'i enjekte edin.
    *   **Adım 4.3.3:** Dosya taraması başarılı olduğunda (`scanResult.IsInfected == false`) `_meteringService.RecordUsageAsync` metodunu çağırın. Bu çağrı aşağıdaki gibi olmalıdır:
        ```csharp
        // Dosya temizse kullanım ölçümlemesi yap.
        var idempotencyKey = $"{fileEntity.StorageObjectName}:{fileEntity.Checksum}";
        await meteringService.RecordUsageAsync(
            fileEntity.TenantId,
            "storage_bytes", // Ölçüm anahtarı
            fileEntity.Size,   // Miktar
            "file_upload_successful", // Kaynak
            idempotencyKey);   // Mükerrer kaydı önleyen anahtar
        ```

4.  **Periyodik Agregasyon Görevlerini Oluşturma (Worker):**
    *   **Adım 4.4.1:** `SBSaaS.Worker` projesine zamanlanmış görevler için bir kütüphane ekleyin (Örn: `Quartz.NET`). `dotnet add package Quartz.Extensions.Hosting`.
    *   **Adım 4.4.2:** `DailyAggregationJob.cs` adında yeni bir `IJob` sınıfı oluşturun. Bu sınıf, `usage_event` tablosundan `usage_daily` tablosuna günlük özetlemeyi yapacak SQL mantığını EF Core ile implemente etmelidir.
    *   **Adım 4.4.3:** `PeriodAggregationJob.cs` adında ikinci bir `IJob` sınıfı oluşturun. Bu sınıf, `usage_daily` verilerini `billing.subscription` ile birleştirerek `usage_period` tablosunu doldurmalıdır.
    *   **Adım 4.4.4:** `PeriodClosingJob.cs` adında üçüncü bir `IJob` sınıfı oluşturun. Bu sınıf, dönemi biten `usage_period` kayıtlarını işleyip, limit aşımı varsa `billing.overage` tablosuna kayıt atmalı ve kaydı `closed=true` olarak işaretlemelidir.
    *   **Adım 4.4.5:** `SBSaaS.Worker` projesinin `Program.cs` dosyasında Quartz.NET'i yapılandırarak bu üç job'ı belirli aralıklarla (örn: her gece) çalışacak şekilde zamanlayın.

---

## İş Paketi 4.2: Rate Limiting (Kota) Yönetimi

**Amaç:** API uç noktalarına kiracı ve plan bazlı erişim limitleri koymak ve bu limitlerin admin arayüzünden yönetilebilmesini sağlamak.

**Referans Doküman:** `610_sbsaas_f_1_feature_flag_limit_enforcement_plan_ozellikleri_kota_overage.md`

### Adım Adım Kırılım:

1.  **Veritabanı ve Servisleri Hazırlama:**
    *   **Adım 4.5.1:** `610_sbsaas_f_1...` belgesindeki `billing.feature_override` ve `billing.quota_usage` tablolarına karşılık gelen Entity'leri ve `DbContext` güncellemelerini yapın. Yeni bir EF migration oluşturup veritabanını güncelleyin.
    *   **Adım 4.5.2:** `IFeatureService` arayüzünü ve cache destekli `FeatureService` implementasyonunu `Application` ve `Infrastructure` katmanlarında oluşturun. `IMemoryCache` kullanın ve servisi DI container'a kaydedin.

2.  **API'de Kota Uygulamasını Geliştirme:**
    *   **Adım 4.6.1:** `SBSaaS.API` projesinde `Filters` adında bir klasör oluşturun. İçine `QuotaAttribute.cs` adında, `IAsyncActionFilter`'dan türeyen bir sınıf ekleyin.
    *   **Adım 4.6.2:** `QuotaAttribute` içinde, `OnActionExecutionAsync` metodunu implemente edin. Bu metod, `IFeatureService`'ten limiti, `SbsDbContext`'ten mevcut kullanımı okumalıdır.
    *   **Adım 4.6.3:** Limit aşılmışsa, `context.Result`'ı `StatusCode = 429` olan bir `ObjectResult` olarak ayarlayın.
    *   **Adım 4.6.4:** Limit aşılmamışsa, `quota_usage` tablosundaki sayacı artırın ve `await next()` ile isteğin devam etmesini sağlayın.
    *   **Adım 4.6.5:** Korumak istediğiniz API endpoint'lerinin (örn: `FilesPresignController`) üzerine `[Quota("api_calls", 10000)]` şeklinde attribute'ü ekleyin.

3.  **Limit Yönetimi için Admin API'si Oluşturma:**
    *   **Adım 4.7.1:** `SBSaaS.API` projesinde `AdminFeaturesController.cs` adında yeni bir controller oluşturun. Bu controller'ı sadece admin yetkisine sahip kullanıcıların erişebileceği şekilde `[Authorize(Roles="Admin")]` ile koruyun.
    *   **Adım 4.7.2:** Bir kiracıya özel limit tanımlamak/güncellemek için bir `POST` veya `PUT` endpoint'i oluşturun. Bu endpoint, `billing.feature_override` tablosuna kayıt atacak ve `IMemoryCache`'deki ilgili tenant'ın cache'ini temizleyecektir.
    *   **Adım 4.7.3:** Bir kiracının mevcut limitlerini listelemek için bir `GET` endpoint'i oluşturun.
