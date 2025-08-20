using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SBSaaS.Application.Interfaces;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SBSaaS.API.Controllers;

// Data Transfer Objects (DTOs) for Presigned URL operations
public record PresignUploadRequest(string FileName, string ContentType, long SizeBytes);
public record PresignUploadResponse(string Url, string ObjectName, string Bucket, int ExpiresSeconds);
public record PresignGetRequest(string ObjectName);
public record PresignGetResponse(string Url, int ExpiresSeconds);


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
    {
        _signer = signer;
        _policy = policy;
        _tenant = tenant;
        _cfg = cfg;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> PresignUpload([FromBody] PresignUploadRequest req, CancellationToken ct)
    {
        // 1. Validate file policy
        if (!_policy.IsAllowed(req.ContentType, req.SizeBytes))
            return BadRequest(new { error = "File type or size not allowed." });

        // 2. Generate a unique, tenant-isolated object name
        var bucket = _cfg["Minio:Bucket"] ?? "sbs-objects";
        var prefix = _policy.BuildTenantPrefix(_tenant.TenantId, DateTimeOffset.UtcNow);
        var ext = Path.GetExtension(req.FileName);
        var objectName = $"{prefix}{Guid.NewGuid():N}{ext}";

        // 3. Create the presigned URL for PUT operation
        var expSec = _cfg.GetValue<int>("Minio:Presign:UploadExpiresSeconds", 600);
        var url = await _signer.PresignPutAsync(bucket, objectName, TimeSpan.FromSeconds(expSec), req.ContentType, ct);

        return Ok(new PresignUploadResponse(url, objectName, bucket, expSec));
    }

    [HttpPost("get")]
    public async Task<IActionResult> PresignGet([FromBody] PresignGetRequest req, CancellationToken ct)
    {
        // Security check: Ensure the requested object belongs to the current tenant.
        if (!req.ObjectName.StartsWith($"tenants/{_tenant.TenantId:D}/"))
        { 
            return Forbid();
        }

        var bucket = _cfg["Minio:Bucket"] ?? "sbs-objects";
        var expSec = _cfg.GetValue<int>("Minio:Presign:DownloadExpiresSeconds", 600);
        var url = await _signer.PresignGetAsync(bucket, req.ObjectName, TimeSpan.FromSeconds(expSec), ct);

        return Ok(new PresignGetResponse(url, expSec));
    }
}
