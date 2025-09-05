using SBSaaS.Domain.Entities.Auth;

namespace SBSaaS.API.Auth;

/// <summary>
/// JWT tabanlı access ve refresh token üretmek için kullanılan servisin arayüzü.
/// </summary>
public interface ITokenService
{
    (string accessToken, string refreshToken, DateTime expires) Issue(ApplicationUser user, IEnumerable<string> roles);
}