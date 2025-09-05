namespace SBSaaS.Application.Interfaces;

/// <summary>
/// Dosya yükleme politikalarını ve kurallarını yöneten servisi soyutlayan arayüz.
/// </summary>
public interface IUploadPolicy
{
    bool IsAllowed(string contentType, long sizeBytes);
    string BuildTenantPrefix(Guid tenantId, DateTimeOffset now);
}