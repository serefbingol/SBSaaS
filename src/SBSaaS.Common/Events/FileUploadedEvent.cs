using System.Text.Json.Serialization;

namespace SBSaaS.Common.Events;

public class FileUploadedEvent
{
    [JsonPropertyName("Key")]
    public string FilePath { get; set; }

    [JsonPropertyName("BucketName")]
    public string BucketName { get; set; }

    [JsonPropertyName("ETag")]
    public string ETag { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class MinioEventRecord
{
    [JsonPropertyName("s3")]
    public FileUploadedEvent S3 { get; set; }

    [JsonPropertyName("eventName")]
    public string EventName { get; set; }
}