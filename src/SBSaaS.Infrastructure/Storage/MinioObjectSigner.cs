using Minio;
using Minio.DataModel.Args;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Storage;

/// <summary>
/// IObjectSigner arayüzünün, MinIO SDK'sını kullanarak presigned URL üreten implementasyonu.
/// </summary>
public class MinioObjectSigner : IObjectSigner
{
    private readonly IMinioClient _minioClient;

    public MinioObjectSigner(IMinioClient minioClient)
    {
        _minioClient = minioClient;
    }

    public async Task<string> PresignPutAsync(string bucket, string objectName, TimeSpan expiry, string? contentType, CancellationToken ct)
    {
        var args = new PresignedPutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithExpiry((int)expiry.TotalSeconds);

        return await _minioClient.PresignedPutObjectAsync(args).ConfigureAwait(false);
    }

    public async Task<string> PresignGetAsync(string bucket, string objectName, TimeSpan expiry, CancellationToken ct)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithExpiry((int)expiry.TotalSeconds);

        return await _minioClient.PresignedGetObjectAsync(args).ConfigureAwait(false);
    }
}