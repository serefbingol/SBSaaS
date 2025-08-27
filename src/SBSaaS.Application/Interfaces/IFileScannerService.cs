namespace SBSaaS.Application.Interfaces;

/// <summary>
/// Defines a service for scanning files.
/// </summary>
public interface IFileScannerService
{
    /// <summary>
    /// Arka planda dosya tarama işlemlerini yürüten servisin arayüzü.
    /// </summary>
    /// <param name="bucketName">The name of the bucket.</param>
    /// <param name="objectName">The name/key of the object to scan.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ScanFileAsync(string bucketName, string objectName, CancellationToken cancellationToken);
}




