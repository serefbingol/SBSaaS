# PROJE WORKER: MİNİO ENTEGRASYONU VE ASENKRON DOSYA İŞLEME

`SBSaaS.Worker` projesi, sistemin "reaktif" ve "otonom" beynidir. Kullanıcıdan doğrudan istek almayan ama sistemdeki olaylara tepki veren veya zamanlanmış görevleri yürüten kritik bir bileşendir.

Bu doküman, MinIO'dan gelen bir dosya oluşturma olayı ile tetiklenen asenkron iş akışının ideal halini açıklamaktadır.

### Genel Felsefe ve Sorumluluklar

`Worker` projesinin temel felsefesi, ana API'yi meşgul etmeden, uzun süren, zaman alan veya harici sistemlerle (MinIO, ClamAV, Stripe vb.) haberleşme gerektiren işlemleri **asenkron** olarak ve **güvenilir** bir şekilde yürütmektir. Bu, sistemin genel performansını ve dayanıklılığını artırır.

Bu asenkron dosya işleme sürecinde `Worker`'ın sorumlulukları şunlardır:

1.  **Olayı Karşılama:** MinIO'dan gelen "yeni dosya yüklendi" olayını (event) bir mesaj kuyruğu üzerinden güvenilir bir şekilde kabul etmek.
2.  **Doğrulama ve Güvenlik:** Yüklenen dosyanın güvenli olduğundan (virüs taraması) emin olmak.
3.  **İzlenebilirlik (Audit):** Dosya üzerinde yapılan her önemli eylemi (tarandı, silindi vb.) denetim kaydına işlemek.
4.  **Ölçümleme (Metering):** Dosya yükleme işlemini, müşterinin kullanım kotasına (örneğin, depolama alanı) yansıtmak.
5.  **Hata Yönetimi:** İşlem sırasında oluşabilecek hataları yönetip, gerekirse tekrar denemek veya alarm üretmek.

### Adım Adım İdeal Akış

**Adım 1: Olayın Alınması ve Kuyruğa Atılması**

-   **Olay:** Bir kullanıcı, API'den aldığı "presigned URL" ile MinIO'ya bir dosya yükler.
-   **Tetiklenme:** MinIO, bu yeni nesne oluşturma olayını (`s3:ObjectCreated:*`) yakalar ve bu bildirimi yapılandırılmış bir mesajlaşma sistemine (örn: RabbitMQ, SQS) iletir. `Worker` servisi, bu sistemdeki ilgili kuyruğu dinler.
-   **Worker'ın Rolü:**
    1.  Gelen mesajı (örn: dosya adı ve bucket bilgisi içeren JSON) alır ve temel bir geçerlilik kontrolü yapar.
    2.  **En Kritik Adım:** Asıl işi yapacak olan `IFileScannerService.ScanFileAsync` metodunu, `IBackgroundTaskQueue` aracılığıyla (veya doğrudan kendisi işleyerek) tetikler.
    3.  Mesajlaşma sistemine, görevin başarıyla alındığını ve işleme konulacağını bildirir (acknowledgement). Bu sayede mesaj kuyruktan silinir ve aynı işin tekrar tekrar yapılmasının önüne geçilir.

**Adım 2: Arka Plan Görevinin Devralınması ve Hazırlık**

-   **Olay:** Arka planda çalışan bir mekanizma (Hosted Service veya doğrudan olay işleyici), `ScanFileAsync` görevini çalıştırmaya başlar.
-   `**FileScannerService**`**'in İlk Adımları:**
    1.  **Loglama Kapsamı (Logging Scope):** İşlemin başından sonuna kadar takip edilebilmesi için `CorrelationId`, `BucketName` ve `ObjectName` gibi bilgilerle bir loglama kapsamı oluşturur.
    2.  **Metadata'yı Oku (A5):** MinIO'dan dosyanın kendisini değil, önce **metadata'sını** ister (`StatObjectAsync`). Bu metadata, iş mantığı için zorunlu olan şu bilgileri içerir:
        -   `x-amz-meta-tenant-id`: Bu dosyanın hangi kiracıya ait olduğu. **Bu bilgi olmadan işlem devam etmemelidir.**
        -   `x-amz-meta-uploaded-by-user-id`: Dosyayı kimin yüklediği (denetim kayıtları için).
        -   Dosyanın boyutu (`size`): Kullanım ölçümü için gereklidir.

**Adım 3: Güvenlik Taraması (A5 & B1)**

-   **Olay:** `FileScannerService`, Docker ağındaki `sbsaas_clamav` servisine bağlanır.
-   **İşlem:** Dosyayı MinIO'dan **stream** olarak okur ve doğrudan ClamAV servisine tarama için gönderir.
-   **Sonuç:** Tarama sonucu alınır (`Clean` veya `Infected`).

**Adım 4: Karar ve Aksiyon (İş Mantığı)**

-   **Senaryo A: Dosya TEMİZ (**`**Clean**`**)**
    1.  **Kullanım Ölçümlemesi (F2):** `IMeteringService` çağrılır.
        -   `RecordUsageAsync(tenantId, "storage_bytes", size, "storage_event", idempotencyKey)`
        -   `idempotencyKey` olarak dosyanın `ETag`'i veya `ObjectName`+`VersionId` kombinasyonu kullanılır. Bu, aynı olayın iki kez işlenmesi durumunda kullanımın mükerrer sayılmasını engeller.
    2.  **Denetim Kaydı (A2):** Başarılı tarama ve saklama işlemi için bir denetim kaydı oluşturulur.
-   **Senaryo B: Dosya VİRÜSLÜ (**`**Infected**`**)**
    1.  **Dosyayı Sil:** `FileScannerService`, MinIO'ya komut göndererek virüslü dosyayı derhal siler.
    2.  **Denetim Kaydı (A2):** Dosyanın neden silindiğine dair çok net bir denetim kaydı oluşturulur.
    3.  **Bildirim (Opsiyonel ama Önerilir):** İlgili kişilere bildirim gönderilebilir.

**Adım 5: Hata Yönetimi ve Dayanıklılık**

-   Tüm `ScanFileAsync` süreci `try-catch` bloğu içinde olmalıdır.
-   Bağımlı bir servis geçici olarak ayakta değilse, mesajlaşma sisteminin **tekrar deneme (retry)** mekanizmaları (örn: exponential backoff) kullanılmalıdır.
-   Tüm denemelere rağmen işlem kalıcı olarak başarısız olursa, bu mesaj bir **"dead-letter queue" (işlenemeyen mesajlar kuyruğu)**'na taşınmalı ve operasyon ekibine bir **alarm** (alert) gönderilmelidir.

Bu bütünsel akış, **Güvenlik (A5)**, **Docker Mimarisi (B1)**, **Denetim (A2)** ve **Kullanım Ölçümleme (F2)** gibi temel taşları birbirine bağlayarak sağlam, güvenli ve ölçeklenebilir bir arka plan işleme altyapısı oluşturur.