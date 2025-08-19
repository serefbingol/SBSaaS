using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBSaaS.API.Auth;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Entities.Invitations;
using SBSaaS.Infrastructure.Persistence;

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

    public AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
                          RoleManager<IdentityRole> roles, ITokenService tokens, SbsDbContext db)
    { _users = users; _signIn = signIn; _roles = roles; _tokens = tokens; _db = db; }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email);
        if (user is null) return Unauthorized();
        var res = await _signIn.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!res.Succeeded) return Unauthorized();
        var roles = await _users.GetRolesAsync(user);
        var (access, refresh, expires) = _tokens.Issue(user, roles);
        // TODO: refresh tokenı DB'ye hashleyip sakla
        return Ok(new { accessToken = access, refreshToken = refresh, expires });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshDto dto)
    {
        // TODO: dto.RefreshToken hash kontrolü, süre ve revoke durumunu DB'den kontrol et.
        // Kullanıcı ve roller alınır, yeni token üretilir
        return Unauthorized();
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
}

public record LoginDto(string Email, string Password);
public record RefreshDto(string RefreshToken);
public record InviteDto(Guid TenantId, string Email, string Role);
public record AcceptInviteDto(string Token, string Password);
