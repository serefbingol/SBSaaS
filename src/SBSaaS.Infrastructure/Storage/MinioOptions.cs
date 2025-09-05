namespace SBSaaS.Infrastructure.Storage;

/// <summary>
/// appsettings.json dosyasındaki "Minio" bölümünü temsil eden ayarlar sınıfı.
/// </summary>
public class MinioOptions
{
    public const string SectionName = "Minio";

    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public bool UseSSL { get; set; }

    public PresignOptions Presign { get; set; } = new();
    public PolicyOptions Policy { get; set; } = new();
}

public class PresignOptions { public int UploadExpiresSeconds { get; set; } = 600; public int DownloadExpiresSeconds { get; set; } = 600; }

public class PolicyOptions { public int MaxSizeMB { get; set; } = 25; public string[] AllowedMime { get; set; } = Array.Empty<string>(); }