using SBSaaS.Application.Interfaces;
using System.Security.Claims;

namespace SBSaaS.API.Services;

/// <summary>
/// Implements ICurrentUser by retrieving user information from the current HttpContext.
/// </summary>
public class HttpContextUserContext : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current user's ID from the 'NameIdentifier' claim.
    /// Returns null if the user is not authenticated or the claim is not present.
    /// </summary>
    public Guid? UserId
    {
        get
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }
    }
}