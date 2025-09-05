using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SBSaaS.API.Auth;
using SBSaaS.API.Contracts.Auth;
using SBSaaS.API.Middleware;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Entities.Auth;
using SBSaaS.Domain.Entities.Invitations;
using SBSaaS.Infrastructure.Persistence;
using System.Security.Cryptography;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ITokenService _tokenService;
    private readonly SbsDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<IdentityRole> roleManager,
        ITokenService tokenService,
        SbsDbContext dbContext,
        ITenantContext tenantContext,
        IOptions<JwtOptions> jwtOptions)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _tokenService = tokenService;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [AllowAnonymousTenant] // Bu endpoint tenant kimliği gerektirmez.
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Unauthorized(new { error = "Geçersiz e-posta veya parola." });
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Unauthorized(new { error = "Geçersiz e-posta veya parola." });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, refreshToken, expires) = _tokenService.Issue(user, roles);

        // Refresh token'ı ve geçerlilik süresini veritabanında sakla.
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);
        await _userManager.UpdateAsync(user);

        return Ok(new AuthResponse(accessToken, refreshToken, expires));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [AllowAnonymousTenant] // Bu endpoint tenant kimliği gerektirmez.
    public async Task<IActionResult> Refresh([FromBody] TokenRefreshRequest request)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.RefreshToken == request.RefreshToken);

        if (user is null || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
        {
            return Unauthorized(new { error = "Geçersiz veya süresi dolmuş yenileme token'ı." });
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, newRefreshToken, expires) = _tokenService.Issue(user, roles);

        // Yeni refresh token'ı veritabanında güncelle (Token Rotasyonu).
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenDays);
        await _userManager.UpdateAsync(user);

        return Ok(new AuthResponse(accessToken, newRefreshToken, expires));
    }

    [HttpPost("invite")]
    [Authorize(Roles = "Admin,Owner")] // Sadece Admin ve Owner rolündekiler davet gönderebilir.
    public async Task<IActionResult> Invite([FromBody] InviteUserRequest request)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new { error = "Geçerli bir kiracı bağlamı bulunamadı. 'X-Tenant-Id' başlığını kontrol edin." });
        }

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = request.Email,
            Role = request.Role,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_'), // URL-safe token
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        _dbContext.Invitations.Add(invitation);
        await _dbContext.SaveChangesAsync();

        // TODO: E-posta gönderme servisi aracılığıyla davet linkini gönder.

        return Ok(new { message = "Davet başarıyla gönderildi." });
    }

    [HttpPost("accept-invite")]
    [AllowAnonymous]
    [AllowAnonymousTenant] // Bu endpoint tenant kimliği gerektirmez.
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
    {
        var invitation = await _dbContext.Invitations
            .FirstOrDefaultAsync(i => i.Token == request.Token && !i.Accepted && i.ExpiresUtc > DateTimeOffset.UtcNow);

        if (invitation is null)
        {
            return BadRequest(new { error = "Geçersiz veya süresi dolmuş davet token'ı." });
        }

        var user = await _userManager.FindByEmailAsync(invitation.Email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = invitation.Email,
                Email = invitation.Email,
                TenantId = invitation.TenantId,
                EmailConfirmed = true // Davetle geldiği için e-postayı doğrulanmış kabul edebiliriz.
            };
            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded) return BadRequest(result.Errors);
        }

        if (!string.IsNullOrWhiteSpace(invitation.Role))
        {
            if (!await _roleManager.RoleExistsAsync(invitation.Role)) await _roleManager.CreateAsync(new IdentityRole(invitation.Role));
            if (!await _userManager.IsInRoleAsync(user, invitation.Role)) await _userManager.AddToRoleAsync(user, invitation.Role);
        }

        invitation.Accepted = true;
        await _dbContext.SaveChangesAsync();

        return Ok(new { message = "Davet başarıyla kabul edildi. Şimdi giriş yapabilirsiniz." });
    }
}