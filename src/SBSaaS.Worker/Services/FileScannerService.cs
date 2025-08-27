using SBSaaS.Application.Interfaces;

namespace SBSaaS.Worker.Services;

/// <summary>
/// IFileScannerService arayüzünü uygulayan ve arka plan görevleri tarafından kullanılan servis.
/// </summary>
public class FileScannerService : IFileScannerService
{
    private readonly ILogger<FileScannerService> _logger;

    // Gerçek bir senaryoda buraya ClamAV istemcisi, IFileStorage gibi bağımlılıklar eklenir.
    public FileScannerService(ILogger<FileScannerService> logger)
    {
        _logger = logger;
    }

    public async Task ScanFileAsync(string bucketName, string objectName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Dosya tarama görevi başlatıldı: Bucket='{Bucket}', Object='{Object}'", bucketName, objectName);

        // Tarama işlemini simüle etmek için 5 saniye bekle.
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        _logger.LogInformation("Dosya tarama görevi tamamlandı: Bucket='{Bucket}', Object='{Object}'", bucketName, objectName);
    }
}