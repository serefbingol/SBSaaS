using Microsoft.Extensions.Primitives;

namespace SBSaaS.API.Middleware;

/// <summary>
/// Gelen isteklerde 'X-Tenant-Id' başlığının varlığını kontrol eden bir ara katman.
/// Belirli yollar (path) bu kontrolden muaf tutulabilir.
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<string> _excludedPaths;

    public TenantMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        // appsettings.json'dan muaf tutulacak yolları oku.
        // Örnek: "Middleware": { "Tenant": { "ExcludedPaths": ["/healthz", "/swagger"] } }
        _excludedPaths = configuration.GetSection("Middleware:Tenant:ExcludedPaths").Get<string[]>() ?? Array.Empty<string>();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Endpoint'e özgü metadata kontrolü. Bu, en esnek yöntemdir.
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<AllowAnonymousTenantAttribute>() != null)
        {
            await _next(context);
            return;
        }

        // 2. Endpoint bulunamayan durumlar (örn: Swagger UI) için path tabanlı kontrol.
        var path = context.Request.Path.Value ?? string.Empty;
        if (_excludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // 3. Başlık kontrolü. Başlığın varlığını, boş olmadığını ve geçerli bir GUID olduğunu doğrula.
        if (!context.Request.Headers.TryGetValue("X-Tenant-Id", out StringValues tenantIdHeader) ||
            StringValues.IsNullOrEmpty(tenantIdHeader) ||
            !Guid.TryParse(tenantIdHeader, out _))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "'X-Tenant-Id' başlığı zorunludur ve geçerli bir GUID olmalıdır." });
            return;
        }

        // Her şey yolundaysa, sonraki middleware'e geç.
        await _next(context);
    }
}