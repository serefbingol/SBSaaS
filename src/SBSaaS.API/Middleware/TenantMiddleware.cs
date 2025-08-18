namespace SBSaaS.API.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Bu katı bir kontroldür. Bazı endpoint'ler (login, public sayfalar, health, swagger)
        // için bu kontrolü atlamak isteyebilirsiniz.
        if (!context.Request.Path.StartsWithSegments("/health") &&
            !context.Request.Path.StartsWithSegments("/swagger") &&
            !context.Request.Headers.ContainsKey("X-Tenant-Id"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "X-Tenant-Id header is required." });
            return;
        }
        await _next(context);
    }
}

