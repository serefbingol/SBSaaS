Bu belge **A5 – MinIO Entegrasyonu** iş paketinin uçtan uca uygulamasını içerir. Amaç, dosya yönetimi için **MinIO** ile güvenli, çok kiracılı (tenant-bazlı) bir yapı kurmak; presigned URL’ler, MIME/size doğrulama, bucket/prefix stratejisi ve örnek upload/download akışlarını sağlamaktır.

---

# 0) DoD – Definition of Done
- MinIO bağlantısı (`MinioClient`) yapılandırıldı.
- Her tenant için ayrı bucket veya prefix stratejisi uygulandı.
- Dosya yükleme/indirme için presigned URL endpoint’leri çalışıyor.
- MIME tipi ve dosya boyutu doğrulama yapılıyor.
- Tüm aksiyonlar **Audit Logging** ile kaydediliyor.
- G1/OpenAPI şeması bu endpoint’leri içeriyor.

---

# 1) NuGet Paketleri
```bash
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Minio
```

---

# 2) appsettings.json – MinIO Ayarları
```json
{
  "Minio": {
    "Endpoint": "localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "UseSSL": false,
    "DefaultBucket": "sbsaas"
  }
}
```

---

# 3) Service Registration
```csharp
builder.Services.AddSingleton<IMinioService, MinioService>();
```

---

# 4) MinIO Servis Katmanı
**`src/SBSaaS.Infrastructure/Storage/MinioService.cs`**
```csharp
using Minio;
using Minio.DataModel;
using SBSaaS.Application.Interfaces;

public class MinioService : IMinioService
{
    private readonly MinioClient _client;
    private readonly string _defaultBucket;

    public MinioService(IConfiguration cfg)
    {
        _defaultBucket = cfg["Minio:DefaultBucket"] ?? "sbsaas";
        _client = new MinioClient()
            .WithEndpoint(cfg["Minio:Endpoint"]!)
            .WithCredentials(cfg["Minio:AccessKey"]!, cfg["Minio:SecretKey"]!)
            .WithSSL(bool.Parse(cfg["Minio:UseSSL"] ?? "false"))
            .Build();
    }

    public async Task EnsureBucketExistsAsync(string bucket)
    {
        bool found = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket));
        if (!found)
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
    }

    public async Task<string> GetPresignedPutUrlAsync(string bucket, string objectName, int expirySeconds = 300)
    {
        await EnsureBucketExistsAsync(bucket);
        return await _client.PresignedPutObjectAsync(new PresignedPutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithExpiry(expirySeconds));
    }

    public async Task<string> GetPresignedGetUrlAsync(string bucket, string objectName, int expirySeconds = 300)
    {
        return await _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithExpiry(expirySeconds));
    }
}
```

---

# 5) Tenant Bazlı Bucket/Prefix Stratejisi
- **Bucket başına tenant**: `sbsaas-{tenantId}`
- **Prefix başına tenant**: Tek bucket + `tenantId/` prefix

Performans ve yönetim için prefix yaklaşımı önerilir.

---

# 6) API Endpoint Örneği
```csharp
[ApiController]
[Route("api/v1/files")]
public class FilesController : ControllerBase
{
    private readonly IMinioService _minio;
    private readonly ITenantContext _tenant;

    public FilesController(IMinioService minio, ITenantContext tenant)
    { _minio = minio; _tenant = tenant; }

    [HttpPost("upload-url")]
    public async Task<IActionResult> GetUploadUrl([FromQuery] string fileName)
    {
        // MIME ve boyut doğrulama burada yapılabilir
        var bucket = "sbsaas";
        var objectName = $"{_tenant.TenantId}/{Guid.NewGuid()}_{fileName}";
        var url = await _minio.GetPresignedPutUrlAsync(bucket, objectName);
        return Ok(new { url, objectName });
    }

    [HttpGet("download-url")]
    public async Task<IActionResult> GetDownloadUrl([FromQuery] string objectName)
    {
        var bucket = "sbsaas";
        var url = await _minio.GetPresignedGetUrlAsync(bucket, objectName);
        return Ok(new { url });
    }
}
```

---

# 7) MIME ve Boyut Doğrulama
- **Whitelist MIME listesi**: `image/png`, `image/jpeg`, `application/pdf`
- Boyut limiti: appsettings ile yönetilir.

---

# 8) Testler
- Presigned URL ile PUT → MinIO console’dan doğrulama
- Yanlış MIME → 415 Unsupported Media Type
- Fazla boyut → 413 Payload Too Large

---

# 9) Sonraki Paket
- **B1 – Docker Compose**: PostgreSQL + MinIO + API entegre ortam

