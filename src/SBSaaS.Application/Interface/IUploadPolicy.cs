namespace SBSaaS.Application.Interfaces;

public interface IUploadPolicy
{
    bool IsAllowed(string contentType, long sizeBytes);
    string BuildTenantPrefix(Guid tenantId, DateTimeOffset now);
}