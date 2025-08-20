using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SBSaaS.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileStorage _files;
    private readonly IUploadPolicy _policy;
    private readonly ITenantContext _tenant;
    private readonly IConfiguration _config;

    public FilesController(
        IFileStorage files,
        IUploadPolicy policy,
        ITenantContext tenant,
        IConfiguration config)
    {
        _files = files;
        _policy = policy;
        _tenant = tenant;
        _config = config;
    }

    [HttpPost("upload")]
    [ProducesResponseType(typeof(OkObjectResult), 200)]
    [ProducesResponseType(typeof(BadRequestObjectResult), 400)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string documentType = "uncategorized")
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "File is required." });

        // 1. Policy Validation
        if (!_policy.IsAllowed(file.ContentType, file.Length))
            return BadRequest(new { error = $"File type '{file.ContentType}' or size is not allowed." });

        // 2. Object Name and Path Generation
        var prefix = _policy.BuildTenantPrefix(_tenant.TenantId, DateTimeOffset.UtcNow);
        var extension = Path.GetExtension(file.FileName);
        var objectName = $"{prefix}{Guid.NewGuid():N}{extension}";
        var bucket = _config["Minio:Bucket"] ?? "sbs-objects";

        // 3. Metadata Preparation
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown_user";
        var metadata = new Dictionary<string, string>
        {
            { "x-amz-meta-tenant-id", _tenant.TenantId.ToString() },
            { "x-amz-meta-uploaded-by-user-id", userId },
            { "x-amz-meta-original-filename", file.FileName },
            { "x-amz-meta-document-type", documentType },
            { "x-amz-meta-upload-date", DateTime.UtcNow.ToString("o") } // ISO 8601 Format
        };

        // 4. Upload
        await using var stream = file.OpenReadStream();
        await _files.UploadAsync(bucket, objectName, stream, file.ContentType, HttpContext.RequestAborted, metadata);

        return Ok(new { objectName, bucket });
    }
}
