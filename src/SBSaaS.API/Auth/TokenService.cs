using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SBSaaS.Domain.Entities;

namespace SBSaaS.API.Auth;

public interface ITokenService
{
    (string accessToken, string refreshToken, DateTime expires) Issue(ApplicationUser user, IEnumerable<string> roles);
}

public class TokenService : ITokenService
{
    private readonly JwtOptions _opt;
    public TokenService(IOptions<JwtOptions> opt) { _opt = opt.Value; }

    public (string accessToken, string refreshToken, DateTime expires) Issue(ApplicationUser user, IEnumerable<string> roles)
    {
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_opt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_opt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("tenant", user.TenantId.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var jwt = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        var access = new JwtSecurityTokenHandler().WriteToken(jwt);
        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (access, refresh, expires);
    }
}
