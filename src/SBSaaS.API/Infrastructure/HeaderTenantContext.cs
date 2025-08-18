using SBSaaS.Application.Interfaces;

namespace SBSaaS.API.Infrastructure;

public class HeaderTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;
    public HeaderTenantContext(IHttpContextAccessor http) => _http = http;
    public Guid TenantId => Guid.TryParse(_http.HttpContext?.Request.Headers["X-Tenant-Id"], out var id) ? id : Guid.Empty;
}

