using SBSaaS.Application.Interfaces;

namespace SBSaaS.Worker.Services;

/// <summary>
/// Worker servisleri için ITenantContext implementasyonu.
/// TenantId, işlenen mesaja göre dinamik olarak ayarlanabilir.
/// </summary>
public class WorkerTenantContext : ITenantContext
{
    public Guid TenantId { get; private set; }
    public void SetTenantId(Guid tenantId) => TenantId = tenantId;
}

/// <summary>
/// Worker servisleri için ICurrentUser implementasyonu.
/// UserId, işlenen mesaja göre dinamik olarak ayarlanabilir.
/// </summary>
public class WorkerUserContext : ICurrentUser
{
    public Guid? UserId { get; private set; }
    public void SetUserId(Guid? userId) => UserId = userId;
}