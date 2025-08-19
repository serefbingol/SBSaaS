namespace SBSaaS.Application.Interfaces;

public interface IUploadPolicy
{
    long MaxFileSize { get; }
    TimeSpan Expiration { get; }
    IReadOnlySet<string> AllowedMimeTypes { get; }
}
