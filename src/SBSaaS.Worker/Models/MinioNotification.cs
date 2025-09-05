using System.Text.Json.Serialization;

namespace SBSaaS.Worker.Models;

// Bu sınıflar, MinIO'nun RabbitMQ'ya gönderdiği olay bildiriminin JSON yapısını temsil eder.

public record MinioNotification(
    [property: JsonPropertyName("Key")] string Key,
    [property: JsonPropertyName("Records")] List<MinioEventRecord> Records
);

public record MinioEventRecord(
    [property: JsonPropertyName("s3")] S3Data S3
);

public record S3Data(
    [property: JsonPropertyName("bucket")] Bucket Bucket,
    [property: JsonPropertyName("object")] S3Object Object
);

public record Bucket([property: JsonPropertyName("name")] string Name);
public record S3Object([property: JsonPropertyName("key")] string Key, [property: JsonPropertyName("eTag")] string ETag);