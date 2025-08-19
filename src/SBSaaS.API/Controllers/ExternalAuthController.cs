using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SBSaaS.Domain.Entities;
using System.Security.Claims;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/auth/external")]
public class ExternalAuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;

    public ExternalAuthController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users)
    { _signIn = signIn; _users = users; }

    [HttpGet("challenge/{provider}")]
    [AllowAnonymous]
    public IActionResult ChallengeProvider(string provider, [FromQuery] string redirectUri)
    {
        var props = _signIn.ConfigureExternalAuthenticationProperties(provider, redirectUri);
        return Challenge(props, provider);
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback()
    {
        var info = await _signIn.GetExternalLoginInfoAsync();
        if (info == null) return BadRequest("No external login info");

        // E-posta zorunlu, yoksa reddet
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email)) return BadRequest("Email scope required");

        var user = await _users.FindByEmailAsync(email);
        if (user == null)
        {
            // TenantId bağlamı: davet akışı veya admin ataması gerektirir; şimdilik System tenant
            user = new ApplicationUser { UserName = email, Email = email, TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111") };
            var res = await _users.CreateAsync(user);
            if (!res.Succeeded) return BadRequest(res.Errors);
        }
        await _users.AddLoginAsync(user, info);

        // Burada JWT üretip istemciye yönlendirme/yanıt verilebilir
        return Ok(new { email });
    }
}
