using Minio;
using Minio.DataModel.Args;
using SBSaaS.Application.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SBSaaS.Infrastructure.Storage;

public class MinioObjectSigner : IObjectSigner
{
    private readonly IMinioClient _client;
    public MinioObjectSigner(IMinioClient client) => _client = client;

    public async Task<string> PresignPutAsync(string bucket, string objectName, TimeSpan expiry, string? contentType, CancellationToken ct)
    {
        var args = new PresignedPutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithExpiry((int)expiry.TotalSeconds);
        return await _client.PresignedPutObjectAsync(args);
    }

    public async Task<string> PresignGetAsync(string bucket, string objectName, TimeSpan expiry, CancellationToken ct)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithExpiry((int)expiry.TotalSeconds);
        return await _client.PresignedGetObjectAsync(args);
    }
}