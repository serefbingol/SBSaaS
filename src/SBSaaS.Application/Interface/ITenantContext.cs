namespace SBSaaS.Application.Interfaces;

public interface ITenantContext
{
    Guid TenantId { get; }
}