using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SBSaaS.API.Contracts.Files;
using SBSaaS.API.Filters;
using SBSaaS.Application.Interfaces;
using SBSaaS.Infrastructure.Storage;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/files")]
[Authorize] // Bu endpoint'e sadece kimliği doğrulanmış kullanıcılar erişebilir.
public class FilesPresignController : ControllerBase
{
    private readonly IUploadPolicy _uploadPolicy;
    private readonly IObjectSigner _objectSigner;
    private readonly ITenantContext _tenantContext;
    private readonly MinioOptions _minioOptions;

    public FilesPresignController(
        IUploadPolicy uploadPolicy,
        IObjectSigner objectSigner,
        ITenantContext tenantContext,
        IOptions<MinioOptions> minioOptions)
    {
        _uploadPolicy = uploadPolicy;
        _objectSigner = objectSigner;
        _tenantContext = tenantContext;
        _minioOptions = minioOptions.Value;
    }

    /// <summary>
    /// Bir dosya yüklemek için geçici ve güvenli bir URL (presigned URL) oluşturur.
    /// </summary>
    /// <remarks>
    /// İstemci (client), bu endpoint'ten aldığı URL'yi kullanarak dosyayı doğrudan
    /// MinIO'ya bir HTTP PUT isteği ile yükler. Bu, sunucunun dosya verisini
    /// kendi üzerinden geçirmesini engeller ve performansı artırır.
    /// </remarks>
    [HttpPost("presign-upload")]
    [Quota("api_calls", 10000)] // Faz 4.2: Bu endpoint için günlük 10,000 çağrı limiti uygula.
    [ProducesResponseType(typeof(PresignResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPresignedUrlForUpload([FromBody] PresignRequest request, CancellationToken ct)
    {
        if (!_uploadPolicy.IsAllowed(request.ContentType, request.Size))
        {
            return BadRequest(new { error = $"Dosya türü ({request.ContentType}) veya boyutu ({request.Size} bytes) politikalara uygun değil." });
        }

        var prefix = _uploadPolicy.BuildTenantPrefix(_tenantContext.TenantId, DateTimeOffset.UtcNow);
        var objectName = $"{prefix}{Guid.NewGuid():N}{Path.GetExtension(request.FileName)}";

        var expiry = TimeSpan.FromSeconds(_minioOptions.Presign.UploadExpiresSeconds);
        var presignedUrl = await _objectSigner.PresignPutAsync(_minioOptions.Bucket, objectName, expiry, request.ContentType, ct);

        return Ok(new PresignResponse(presignedUrl, objectName));
    }
}