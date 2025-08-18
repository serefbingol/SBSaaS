using Microsoft.AspNetCore.Http;
using SBSaaS.Application.Interfaces;
using System.Security.Claims;

public class HeaderTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;
    public HeaderTenantContext(IHttpContextAccessor http) => _http = http;

    public Guid TenantId
    {
        get
        {
            var httpContext = _http.HttpContext;
            if (httpContext is null) return Guid.Empty;

            // 1. Öncelik: X-Tenant-Id header'ını kontrol et.
            // Bu, genellikle bir API Gateway tarafından eklenir ve en yüksek önceliğe sahiptir.
            if (httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValue) &&
                Guid.TryParse(headerValue, out var tenantIdFromHeader))
            {
                return tenantIdFromHeader;
            }

            // 2. Öncelik (Fallback): Kimliği doğrulanmış kullanıcının JWT claim'ini kontrol et.
            // Bu, doğrudan API çağrıları veya gateway'in başlığı eklemediği durumlar için kullanışlıdır.
            var claimValue = httpContext.User?.FindFirstValue("tenant_id");
            return Guid.TryParse(claimValue, out var tenantIdFromClaim) ? tenantIdFromClaim : Guid.Empty;
        }
    }
}