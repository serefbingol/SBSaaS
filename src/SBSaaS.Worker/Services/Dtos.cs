namespace SBSaaS.Worker.Services;

/// <summary>
/// Represents the payload sent by a MinIO webhook.
/// </summary>
public record MinioEvent(List<MinioRecord> Records);

/// <summary>
/// Represents a single record within a MinIO event.
/// </summary>
public record MinioRecord(string eventName, string key);

/// <summary>
/// A data transfer object representing a file scan request.
/// </summary>
public record MinioScanRequest(string BucketName, string ObjectName);