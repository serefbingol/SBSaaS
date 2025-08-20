namespace SBSaaS.Worker.Services;

public record MinioScanRequest(string BucketName, string ObjectName);
