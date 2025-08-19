using Microsoft.Extensions.Configuration;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Storage;

public class UploadPolicy : IUploadPolicy
{
    private readonly IConfiguration _config;

    public UploadPolicy(IConfiguration config)
    {
        _config = config;
    }

    public long MaxFileSize => long.TryParse(_config["UploadPolicy:MaxFileSizeMb"], out var mb) ? mb * 1024 * 1024 : 10 * 1024 * 1024;

    public TimeSpan Expiration => TimeSpan.FromMinutes(int.TryParse(_config["UploadPolicy:ExpirationMinutes"], out var min) ? min : 5);

    public IReadOnlySet<string> AllowedMimeTypes =>
        new HashSet<string>(_config.GetSection("UploadPolicy:AllowedMimeTypes").Get<string[]>() ?? new[] { "image/jpeg", "image/png", "application/pdf" });
}
