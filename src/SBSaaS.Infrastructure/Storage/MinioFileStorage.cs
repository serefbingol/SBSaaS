using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Storage;

public class MinioFileStorage : IFileStorage
{
    private readonly MinioClient _client;
    public MinioFileStorage(MinioClient client) => _client = client;

    public async Task<string> UploadAsync(string bucket, string objectName, Stream data, string contentType, CancellationToken ct)
    {
        bool found = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct);
        if (!found) await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);

        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectName)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType), ct);
        return objectName;
    }

    public Task<Stream> DownloadAsync(string bucket, string objectName, CancellationToken ct)
    {
        var ms = new MemoryStream();
        return Task.Run(async () =>
        {
            await _client.GetObjectAsync(new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectName)
                .WithCallbackStream(stream => stream.CopyTo(ms)), ct);
            ms.Position = 0;
            return (Stream)ms;
        }, ct);
    }

    public async Task DeleteAsync(string bucket, string objectName, CancellationToken ct)
        => await _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(objectName), ct);
}