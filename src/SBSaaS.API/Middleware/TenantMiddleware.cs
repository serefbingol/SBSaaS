using Microsoft.AspNetCore.Http.Features;

namespace SBSaaS.API.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    public TenantMiddleware(RequestDelegate next) => _next = next;
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.Features.Get<IEndpointFeature>()?.Endpoint;

        // Endpoint, [AllowAnonymousTenant] attribute'u ile işaretlenmişse, tenant kontrolünü atla.
        if (endpoint?.Metadata.GetMetadata<AllowAnonymousTenantAttribute>() != null)
        {
            await _next(context);
            return;
        }

        // X-Tenant-Id başlığının varlığını kontrol et.
        if (!context.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantIdValues))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "X-Tenant-Id header is required." });
            return;
        }

        // X-Tenant-Id başlığının geçerli bir GUID formatında olduğunu doğrula.
        if (!Guid.TryParse(tenantIdValues.FirstOrDefault(), out _))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "X-Tenant-Id header is not a valid GUID." });
            return;
        }

        await _next(context);
    }
}
