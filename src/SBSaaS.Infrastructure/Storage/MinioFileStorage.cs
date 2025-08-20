using Minio;
using Minio.DataModel.Args;
using SBSaaS.Application.Interfaces;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SBSaaS.Infrastructure.Storage;

public class MinioFileStorage : IFileStorage
{
    private readonly IMinioClient _client;
    public MinioFileStorage(IMinioClient client) => _client = client;

    public async Task<string> UploadAsync(string bucket, string objectName, Stream data, string contentType, CancellationToken ct, Dictionary<string, string>? metadata = null)
    {
        var bucketExistsArgs = new BucketExistsArgs().WithBucket(bucket);
        bool found = await _client.BucketExistsAsync(bucketExistsArgs, ct);
        if (!found)
        {
            var makeBucketArgs = new MakeBucketArgs().WithBucket(bucket);
            await _client.MakeBucketAsync(makeBucketArgs, ct);
        }

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType);

        if (metadata != null && metadata.Count > 0)
        {
            putObjectArgs.WithHeaders(metadata);
        }

        await _client.PutObjectAsync(putObjectArgs, ct);
        return objectName;
    }

    public async Task<Stream> DownloadAsync(string bucket, string objectName, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var getObjectArgs = new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithCallbackStream(stream => stream.CopyTo(ms));

        await _client.GetObjectAsync(getObjectArgs, ct);
        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string bucket, string objectName, CancellationToken ct)
    {
        var removeObjectArgs = new RemoveObjectArgs().WithBucket(bucket).WithObject(objectName);
        await _client.RemoveObjectAsync(removeObjectArgs, ct);
    }
}
