using Microsoft.Extensions.Options;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Storage;

/// <summary>
/// IUploadPolicy arayüzünün, MinioOptions'tan gelen yapılandırmaya göre
/// dosya yükleme politikalarını uygulayan implementasyonu.
/// </summary>
public class UploadPolicy : IUploadPolicy
{
    private readonly MinioOptions _options;

    public UploadPolicy(IOptions<MinioOptions> options)
    {
        _options = options.Value;
    }

    public bool IsAllowed(string contentType, long sizeBytes)
    {
        var maxSizeBytes = _options.Policy.MaxSizeMB * 1024 * 1024;
        return _options.Policy.AllowedMime.Contains(contentType, StringComparer.OrdinalIgnoreCase) && sizeBytes <= maxSizeBytes;
    }

    public string BuildTenantPrefix(Guid tenantId, DateTimeOffset now)
    {
        return $"tenant/{tenantId:D}/{now:yyyy/MM}/";
    }
}