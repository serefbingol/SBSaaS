namespace SBSaaS.Application.Interfaces;

/// <summary>
/// Depolama servisindeki nesneler için güvenli, zaman aşımına uğrayan URL'ler (presigned URL)
/// oluşturma işlemini soyutlayan arayüz.
/// </summary>
public interface IObjectSigner
{
    Task<string> PresignPutAsync(string bucket, string objectName, TimeSpan expiry, string? contentType, CancellationToken ct);
    Task<string> PresignGetAsync(string bucket, string objectName, TimeSpan expiry, CancellationToken ct);
}