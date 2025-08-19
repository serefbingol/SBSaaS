using System.Security.Claims;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.API.Services;

public class HttpContextUserContext : IUserContext
{
    private readonly IHttpContextAccessor _http;
    public HttpContextUserContext(IHttpContextAccessor http) => _http = http;

    public string? UserId => _http.HttpContext?.User?.Identity?.IsAuthenticated == true
        ? _http.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? _http.HttpContext.User.Identity?.Name
        : null;
}

