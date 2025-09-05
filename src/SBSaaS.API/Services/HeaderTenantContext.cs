using SBSaaS.Application.Interfaces;

namespace SBSaaS.API.Services;

/// <summary>
/// Implements ITenantContext by retrieving the tenant ID from the 'X-Tenant-Id' request header.
/// </summary>
public class HeaderTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HeaderTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current Tenant ID.
    /// Returns Guid.Empty if the header is not present or invalid.
    /// </summary>
    public Guid TenantId
    {
        get
        {
            var tenantIdHeader = _httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            return Guid.TryParse(tenantIdHeader, out var tenantId) ? tenantId : Guid.Empty;
        }
    }
}
