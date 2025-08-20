using Microsoft.Extensions.Configuration;
using SBSaaS.Application.Interfaces;
using System;
using System.Linq;

namespace SBSaaS.Infrastructure.Storage;

public class UploadPolicy : IUploadPolicy
{
    private readonly IConfiguration _cfg;
    public UploadPolicy(IConfiguration cfg) => _cfg = cfg;

    public bool IsAllowed(string contentType, long sizeBytes)
    {
        var allowed = _cfg.GetSection("Minio:Policy:AllowedMime").Get<string[]>() ?? Array.Empty<string>();
        var maxBytes = (_cfg.GetValue<long?>("Minio:Policy:MaxSizeMB") ?? 25) * 1024 * 1024;

        if (string.IsNullOrEmpty(contentType) || sizeBytes <= 0)
            return false;

        return allowed.Contains(contentType, StringComparer.OrdinalIgnoreCase) && sizeBytes <= maxBytes;
    }

    public string BuildTenantPrefix(Guid tenantId, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));

        return $"tenants/{tenantId:D}/{now:yyyy/MM}/";
    }
}