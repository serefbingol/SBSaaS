using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SBSaaS.API.Auth;
using SBSaaS.Domain.Entities; // For ApplicationUser
using SBSaaS.Domain.Entities.Auth; // Assuming this is needed for other types
using SBSaaS.Domain.Entities.Invitations;
using SBSaaS.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text;
using RefreshTokenEntity = SBSaaS.Domain.Entities.Auth.RefreshToken;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly ITokenService _tokens;
    private readonly SbsDbContext _db;
    private readonly JwtOptions _jwtOptions;

    public AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
                          RoleManager<IdentityRole> roles, ITokenService tokens, SbsDbContext db,
                          IOptions<JwtOptions> jwtOptions)
    {
        _users = users;
        _signIn = signIn;
        _roles = roles;
        _tokens = tokens;
        _db = db;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email);
        if (user is null) return Unauthorized();
        var res = await _signIn.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!res.Succeeded) return Unauthorized();
        var roles = await _users.GetRolesAsync(user);
        var (access, refresh, expires) = _tokens.Issue(user, roles);

        // Save the hashed refresh token to the database
        var refreshTokenEntity = new RefreshTokenEntity
        {
            UserId = user.Id,
            Token = HashToken(refresh),
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        // Assumes a 'DbSet<RefreshTokenEntity> RefreshTokens' exists on SbsDbContext
        _db.Set<RefreshTokenEntity>().Add(refreshTokenEntity);
        await _db.SaveChangesAsync();

        return Ok(new { accessToken = access, refreshToken = refresh, expires });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshDto dto)
    {
        var hashedToken = HashToken(dto.RefreshToken);

        // Find the token in DB by its hash. Include the user for token generation.
        var refreshToken = await _db.Set<RefreshTokenEntity>()
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == hashedToken);

        if (refreshToken is null)
        {
            return Unauthorized(new { error = "Invalid refresh token." });
        }

        if (!refreshToken.IsActive)
        {
            return Unauthorized(new { error = "Refresh token has expired or been revoked." });
        }

        var user = refreshToken.User;
        if (user is null)
        {
            // This should not happen if DB constraints are set up correctly
            return Unauthorized(new { error = "User not found for refresh token." });
        }

        // Token rotation: issue new tokens, revoke old one.
        var roles = await _users.GetRolesAsync(user);
        var (newAccessToken, newRefreshToken, newExpires) = _tokens.Issue(user, roles);

        var newHashedToken = HashToken(newRefreshToken);

        // Revoke the old token and link it to the new one for security auditing
        refreshToken.RevokedUtc = DateTimeOffset.UtcNow;
        refreshToken.ReplacedByToken = newHashedToken;

        // Add the new token
        var newRefreshTokenEntity = new RefreshTokenEntity
        {
            UserId = user.Id,
            Token = newHashedToken,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        _db.Set<RefreshTokenEntity>().Add(newRefreshTokenEntity);

        await _db.SaveChangesAsync();

        return Ok(new { accessToken = newAccessToken, refreshToken = newRefreshToken, expires = newExpires });
    }

    [HttpPost("invite")] // Admin/Owner
    public async Task<IActionResult> Invite([FromBody] InviteDto dto)
    {
        // Davet token üretimi ve kaydı
        var inv = new Invitation
        {
            Id = Guid.NewGuid(),
            TenantId = dto.TenantId,
            Email = dto.Email,
            Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
            Role = dto.Role
        };
        _db.Invitations.Add(inv);
        await _db.SaveChangesAsync();
        // TODO: e-posta gönderimi
        return Ok(new { token = inv.Token });
    }

    [HttpPost("accept")]
    public async Task<IActionResult> Accept([FromBody] AcceptInviteDto dto)
    {
        var inv = await _db.Invitations.FirstOrDefaultAsync(x => x.Token == dto.Token && !x.Accepted && x.ExpiresUtc > DateTimeOffset.UtcNow);
        if (inv is null) return BadRequest("Invalid or expired invitation");

        var user = await _users.FindByEmailAsync(inv.Email);
        if (user == null)
        {
            user = new ApplicationUser { UserName = inv.Email, Email = inv.Email, TenantId = inv.TenantId };
            var created = await _users.CreateAsync(user, dto.Password);
            if (!created.Succeeded) return BadRequest(created.Errors);
        }
        else
        {
            // Mevcut kullanıcıyı tenant'a bağla (çok tenantlı kullanıcı modeli istenirse ek model gerekir)
            user.TenantId = inv.TenantId;
            await _users.UpdateAsync(user);
        }

        if (!string.IsNullOrWhiteSpace(inv.Role))
        {
            if (!await _roles.RoleExistsAsync(inv.Role))
                await _roles.CreateAsync(new IdentityRole(inv.Role));
            await _users.AddToRoleAsync(user, inv.Role);
        }

        inv.Accepted = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }
}

public record LoginDto(string Email, string Password);
public record RefreshDto(string RefreshToken);
public record InviteDto(Guid TenantId, string Email, string Role);
public record AcceptInviteDto(string Token, string Password);
