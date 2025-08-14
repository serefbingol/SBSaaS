using System.Threading.Tasks;

namespace SBSaaS.Application.Interfaces;

public interface IFileStorage
{
    Task<string> UploadAsync(string bucket, string objectName, Stream data, string contentType, CancellationToken ct);
    Task<Stream> DownloadAsync(string bucket, string objectName, CancellationToken ct);
    Task DeleteAsync(string bucket, string objectName, CancellationToken ct);
}