using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.Infrastructure.Storage;

public class MinioFileStorage : IFileStorage
{
    private readonly MinioClient _client;
    private readonly ITenantContext _tenantContext;

    public MinioFileStorage(MinioClient client, ITenantContext tenantContext)
    {
        _client = client;
        _tenantContext = tenantContext;
    }

    public async Task<string> UploadAsync(string bucket, string objectName, Stream data, string contentType, CancellationToken ct)
    {
        EnsureValidTenantContext();
        var tenantScopedObjectName = GetTenantScopedObjectName(objectName);

        bool found = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct);
        if (!found) await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);

        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(tenantScopedObjectName)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType), ct);
        
        // Dışarıya sadece orijinal dosya adını döndür, tenant ön ekini değil.
        return objectName; 
    }

    public async Task<Stream> DownloadAsync(string bucket, string objectName, CancellationToken ct)
    {
        EnsureValidTenantContext();
        var tenantScopedObjectName = GetTenantScopedObjectName(objectName);

        var ms = new MemoryStream();
        
        // Task.Run sarmalayıcısı olmadan, daha verimli bir async/await kullanımı.
        await _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(tenantScopedObjectName)
            .WithCallbackStream(stream => stream.CopyTo(ms)), ct);

        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string bucket, string objectName, CancellationToken ct)
    {
        EnsureValidTenantContext();
        var tenantScopedObjectName = GetTenantScopedObjectName(objectName);
        await _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(tenantScopedObjectName), ct);
    }

    private string GetTenantScopedObjectName(string objectName)
        => $"tenants/{_tenantContext.TenantId}/{objectName}";

    private void EnsureValidTenantContext()
    {
        if (_tenantContext.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("A valid tenant context is required for file storage operations.");
        }
    }
}