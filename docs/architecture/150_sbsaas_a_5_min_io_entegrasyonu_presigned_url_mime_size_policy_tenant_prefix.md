Bu belge **A5 – MinIO Entegrasyonu** iş paketinin uçtan uca uygulamasını içerir. Amaç: MinIO (S3 uyumlu) ile **tenant izole** dosya depolama; hem **doğrudan upload** (stream) hem de **presigned URL** akışları; MIME/boyut doğrulama; opsiyonel antivirüs; CORS ve güvenlik ayarları.

---

# 0) DoD – Definition of Done

- MinIO bağlantısı yapılandırıldı ve **IFileStorage** üzerinden upload/download/delete çalışıyor.
- **Presigned URL** akışı ile istemciden doğrudan (PUT) yükleme ve (GET) indirme mümkün.
- Tenant bazlı **bucket/prefix stratejisi** uygulandı.
- **MIME ve boyut** kısıtları sunucu tarafında doğrulanıyor; uygun hata döndürülüyor.
- CORS ve içerik güvenlik başlıkları dokümante edildi.
- (Opsiyonel) Antivirüs tarama (ClamAV) entegrasyonu için hook noktaları eklendi.
- G1/OpenAPI sözleşmesi presigned uçlarla güncellendi.

---

# 1) Yapı Taşları ve Strateji

**Bucket/Prefix:**

- Tek bucket (ör. `sbs-objects`) + `tenant/{tenantId}/yyyy/MM/` prefix.
- Avantaj: yönetim kolay, politika/yaşam döngüsü tek yerde; dezavantaj: çok büyük listelerde dizin taraması.

**İsimlendirme:**

- Nesne adı: `{prefix}{Guid}{extension}`; orijinal ad metaveri olarak saklanır.

**Saklanan Metadata:**

MinIO (S3) nesneleriyle birlikte saklanacak metadata, uygulamanın işlevselliği, güvenliği ve denetlenebilirliği için kritik öneme sahiptir. Aşağıda zorunlu, önerilen ve senaryoya özel metadata alanları listelenmiştir.

***Zorunlu Metadata (Uygulamanın Temeli İçin):***
- **`x-amz-meta-tenant-id`**: Nesnenin hangi kiracıya ait olduğunu belirtir. Çok kiracılı mimarinin temel taşıdır.
- **`x-amz-meta-uploaded-by-user-id`**: Dosyayı yükleyen kullanıcının sistemdeki benzersiz kimliğidir. Denetim ve sorumluluk takibi için zorunludur.
- **`x-amz-meta-document-type`**: Dosyanın türünü belirtir (örneğin, "sözleşme", "profil-resmi", "fatura"). Kategorizasyon ve arama için önemlidir.

***Önerilen Metadata (İşlevselliği ve UX'i Artıran):***
- **`x-amz-meta-original-filename`**: Kullanıcının yüklediği dosyanın orijinal adını saklar. İndirme sırasında doğru dosya adını sunmak kullanıcı deneyimi için önemlidir.
- **`x-amz-meta-upload-date`**: Dosyanın yüklendiği zamanı (ISO 8601 formatında) belirtir. Raporlama ve yaşam döngüsü yönetimi için faydalıdır.
- **`x-amz-meta-access-control`**: Belgeye kimlerin erişebileceğini belirten bir rol veya izin listesi içerebilir. Daha granüler güvenlik için kullanılabilir.

***Opsiyonel & Senaryoya Özel Metadata:***
- **`x-amz-meta-employee-id`**: Eğer uygulama İK gibi bir alana yönelikse, belgenin ilgili olduğu çalışan kimliğini saklar.
- **`x-amz-meta-department-id`**: Belgenin ait olduğu departmanı belirtir.
- **`x-amz-meta-approval-status`**: Bir iş akışı içindeki belgenin onay durumunu ("onaylandi", "reddedildi") tutar.
- **`x-amz-meta-tags`**: Virgülle ayrılmış etiketler (örn. `rapor,2024,finans`) ile nesneleri daha esnek şekilde sınıflandırmak için kullanılır.

---

# 2) Konfigürasyon

``

```json
{
  "Minio": {
    "Endpoint": "localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "Bucket": "sbs-objects",
    "UseSSL": false,
    "Presign": {
      "UploadExpiresSeconds": 600,
      "DownloadExpiresSeconds": 600
    },
    "Policy": {
      "MaxSizeMB": 25,
      "AllowedMime": [
        "image/png",
        "image/jpeg",
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.oasis.opendocument.text",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/rtf",
        "application/zip",
        "application/x-rar-compressed",
        "text/plain",
        "text/csv",
        "audio/mpeg",
        "audio/wav",
        "video/mp4"
      ]
    }
  }
}
```

---

# 3) Domain/Application – Arayüzlerin genişletilmesi

**Var olan** `IFileStorage` doğrudan stream akışı için yeterliydi. Presigned URL için ilave arayüz ekleyelim:

``

```csharp
namespace SBSaaS.Application.Interfaces;

public interface IObjectSigner
{
    Task<string> PresignPutAsync(string bucket, string objectName, TimeSpan expiry, string? contentType, CancellationToken ct);
    Task<string> PresignGetAsync(string bucket, string objectName, TimeSpan expiry, CancellationToken ct);
}
```

**Politika servisi** – MIME/boyut doğrulaması ve tenant prefix üretimi:

``

```csharp
namespace SBSaaS.Application.Interfaces;

public interface IUploadPolicy
{
    bool IsAllowed(string contentType, long sizeBytes);
    string BuildTenantPrefix(Guid tenantId, DateTimeOffset now);
}
```

---

# 4) Infrastructure – MinIO servisleri

**Signer implementasyonu**
``

```csharp
using Minio;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Storage;

public class MinioObjectSigner : IObjectSigner
{
    private readonly MinioClient _client;
    public MinioObjectSigner(MinioClient client) => _client = client;

    public async Task<string> PresignPutAsync(string bucket, string objectName, TimeSpan expiry, string? contentType, CancellationToken ct)
    {
        // MinIO .NET SDK'da PresignedPutObjectAsync mevcut
        return await _client.PresignedPutObjectAsync(new PresignedPutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithExpiry((int)expiry.TotalSeconds));
    }

    public async Task<string> PresignGetAsync(string bucket, string objectName, TimeSpan expiry, CancellationToken ct)
    {
        return await _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithExpiry((int)expiry.TotalSeconds));
    }
}
```

**Upload Policy**
``

```csharp
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Storage;

public class UploadPolicy : IUploadPolicy
{
    private readonly IConfiguration _cfg;
    public UploadPolicy(IConfiguration cfg) => _cfg = cfg;

    public bool IsAllowed(string contentType, long sizeBytes)
    {
        var allowed = _cfg.GetSection("Minio:Policy:AllowedMime").Get<string[]>() ?? Array.Empty<string>();
        var max = (_cfg.GetSection("Minio:Policy:MaxSizeMB").Get<int?>() ?? 25) * 1024 * 1024;
        return allowed.Contains(contentType) && sizeBytes <= max;
    }

    public string BuildTenantPrefix(Guid tenantId, DateTimeOffset now)
        => $"tenant/{tenantId:D}/{now:yyyy/MM}/";
}
```

**DI Kayıtları**
``

```csharp
services.AddSingleton(sp => new MinioClient()
    .WithEndpoint(config["Minio:Endpoint"]!)
    .WithCredentials(config["Minio:AccessKey"]!, config["Minio:SecretKey"]!)
    .WithSSL(bool.TryParse(config["Minio:UseSSL"], out var ssl) && ssl)
    .Build());

services.AddScoped<IObjectSigner, MinioObjectSigner>();
services.AddScoped<IUploadPolicy, UploadPolicy>();
```

---

# 5) API – Presigned URL uçları

**DTO’lar**
``

```csharp
public record PresignUploadRequest(string FileName, string ContentType, long SizeBytes, string? Folder = null);
public record PresignUploadResponse(string Url, string ObjectName, string Bucket, int ExpiresSeconds);
public record PresignGetRequest(string ObjectName);
public record PresignGetResponse(string Url, int ExpiresSeconds);
```

**Controller**
``

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/files/presign")]
[Authorize]
public class FilesPresignController : ControllerBase
{
    private readonly IObjectSigner _signer;
    private readonly IUploadPolicy _policy;
    private readonly ITenantContext _tenant;
    private readonly IConfiguration _cfg;

    public FilesPresignController(IObjectSigner signer, IUploadPolicy policy, ITenantContext tenant, IConfiguration cfg)
    { _signer = signer; _policy = policy; _tenant = tenant; _cfg = cfg; }

    [HttpPost("upload")]
    public async Task<IActionResult> PresignUpload([FromBody] PresignUploadRequest req, CancellationToken ct)
    {
        if (!_policy.IsAllowed(req.ContentType, req.SizeBytes))
            return BadRequest(new { error = "File type/size not allowed" });

        var bucket = _cfg["Minio:Bucket"] ?? "sbs-objects";
        var prefix = _policy.BuildTenantPrefix(_tenant.TenantId, DateTimeOffset.UtcNow);
        var ext = Path.GetExtension(req.FileName) ?? string.Empty;
        var objectName = $"{prefix}{Guid.NewGuid():N}{ext}";

        var expSec = int.TryParse(_cfg["Minio:Presign:UploadExpiresSeconds"], out var s) ? s : 600;
        var url = await _signer.PresignPutAsync(bucket, objectName, TimeSpan.FromSeconds(expSec), req.ContentType, ct);
        return Ok(new PresignUploadResponse(url, objectName, bucket, expSec));
    }

    [HttpPost("get")]
    public async Task<IActionResult> PresignGet([FromBody] PresignGetRequest req, CancellationToken ct)
    {
        var bucket = _cfg["Minio:Bucket"] ?? "sbs-objects";
        var expSec = int.TryParse(_cfg["Minio:Presign:DownloadExpiresSeconds"], out var s) ? s : 600;
        var url = await _signer.PresignGetAsync(bucket, req.ObjectName, TimeSpan.FromSeconds(expSec), ct);
        return Ok(new PresignGetResponse(url, expSec));
    }
}
```

> Not: Upload sırasında **Content-Type** istemci tarafından header’da gönderilmelidir: `Content-Type: image/png`. Boyut doğrulaması istemci tarafında da yapılmalı.

---

# 6) Doğrudan Upload (stream) uçları – (önceden ekliydi)

İsteyen istemciler presigned yerine API üzerinden de yükleyebilir. (Bkz. `FilesController.Upload`)
**Ek validasyon** ekleyin:

```csharp
if(!_policy.IsAllowed(file.ContentType ?? "application/octet-stream", file.Length))
  return BadRequest("File type/size not allowed");
```

---

# 7) MinIO CORS & Güvenlik Notları

**CORS**: Presigned URL’ler doğrudan MinIO’ya gittiği için **MinIO tarafında CORS** açılmalı. `mc` CLI ile:

```bash
mc alias set local http://localhost:9000 minioadmin minioadmin
mc anonymous set download local/sbs-objects  # sadece GET anonim istenecekse
mc admin config set local/ api cors="*"     # geliştirme amaçlı; üretimde domain kısıtlayın
```

> Üretimde: sadece belirli origin’lere izin verin, `PUT`/`GET` yöntemlerini ve gerekli başlıkları (Content-Type, x-amz-*)" ekleyin.

**Server-Side Encryption (opsiyonel)**: MinIO KMS ile SSE-S3/SSE-KMS kullanabilirsiniz.

**Yaşam Döngüsü Politikası**: Önizleme/tmp klasörleri için otomatik silme (örn. 7 gün) kuralı ekleyin.

---

# 8) Antivirüs (Opsiyonel)

**ClamAV** ile tarama için iki yaklaşım:

1. **API upload** yolunda dosyayı geçici dizine indir → tarat → temizse MinIO’ya yaz.
2. **Event-driven**: MinIO bucket notification (webhook/kuyruk) ile yeni nesne → işleyici servis (Hangfire/Worker) ClamAV ile tarar; temiz değilse siler ve audit’e işler.
Bu projede Event-driven seçilmiştir.

---

# 9) OpenAPI (G1) Güncellemesi – Presign uçları

`/api/v1/files/presign/upload` ve `/api/v1/files/presign/get` eklendi. Örnek şema notu:

```yaml
paths:
  /files/presign/upload:
    post:
      summary: Create presigned PUT url
      tags: [Files]
      security: [{ bearerAuth: [] }]
      parameters: [{ $ref: '#/components/parameters/TenantHeader' }]
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/PresignUploadRequest' }
      responses:
        '200': { description: OK, content: { application/json: { schema: { $ref: '#/components/schemas/PresignUploadResponse' } } } }
```

---

# 10) Test Senaryoları

- **Policy**: Uygun olmayan MIME (örn. `application/x-msdownload`) reddedilmeli.
- **Size**: Limit üzeri dosyada 400 dönmeli, mesajda limit yer almalı.
- **Prefix**: Farklı tenant GUID’leri ile oluşturulan nesneler farklı dizinlere yazılmalı.
- **Presign Upload**: Dönen URL’e `PUT` ile yükleme → `PresignGet` ile indirme doğrulanmalı.
- **Auth**: Presign uçları `Authorize` korumasında ve tenant header zorunlu.

---

# 11) Sonraki Paket

- **B1 – Docker Compose**: PostgreSQL + MinIO + API konteynerları, MinIO bucket/init ve CORS ayarları için başlangıç script’leri; healthcheck ve ağ politikaları.

