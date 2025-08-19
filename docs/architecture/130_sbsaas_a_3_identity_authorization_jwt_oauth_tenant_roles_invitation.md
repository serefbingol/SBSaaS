Bu belge  **A3 – Identity & Authorization**  iş paketinin uçtan uca uygulanabilir kılavuzudur. JWT tabanlı API yetkilendirme, Google/Microsoft OAuth entegrasyonu, tenant-bound kullanıcı-rol-claim modeli, davet (invitation) akışı, refresh token, rate limiting ve data protection konfigürasyonlarını içerir.

----------

## 0) DoD – Definition of Done

-   API,  **JWT Bearer**  ile kimlik doğruluyor; access + refresh token stratejisi çalışıyor.
-   Google/Microsoft OAuth ile dış giriş akışı tamam.
-   Kullanıcılar  **TenantId**  ile ilişkilendirildi; rol/claim modeli tenant bazlı çalışıyor.
-   Davet akışı (invite → accept) ve ilk kullanıcı/rol ataması hazır.
-   Rate limiting, lockout, password & token politikaları tanımlı.
-   Data Protection anahtarları kalıcı (persisted) hale getirildi.
-   OpenAPI (G1) şeması güncellendi.

----------

## 1) NuGet Paketleri (kontrol)

```
# API tarafı
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.Authentication.Google
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.Authentication.MicrosoftAccount
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.Authorization
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.RateLimiting

# Infrastructure tarafı
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.Extensions.Caching.StackExchangeRedis
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.AspNetCore.DataProtection.StackExchangeRedis
```

> Redis opsiyonel; çoklu instance’da DataProtection & ticket paylaşımı için önerilir.

----------

## 2) Domain – ApplicationUser, Roles ve Davet (Invitation)


`**src/SBSaaS.Domain/Entities/ApplicationUser.cs**`

```
using Microsoft.AspNetCore.Identity;

namespace SBSaaS.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public Guid TenantId { get; set; }
    public string? DisplayName { get; set; }
}
```

**Rol isimleri**  (öneri):  `Owner`,  `Admin`,  `Manager`,  `User`,  `Viewer`  
Tenant bazlı yetki için claim’ler:  `tenant`,  `role`,  `perm:*`  (ince taneli izinler için)

`**src/SBSaaS.Domain/Entities/Invitations/Invitation.cs**`

```
namespace SBSaaS.Domain.Entities.Invitations;

public class Invitation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = default!;
    public string Token { get; set; } = default!; // tek kullanımlık
    public DateTimeOffset ExpiresUtc { get; set; }
    public bool Accepted { get; set; }
    public string? Role { get; set; } // varsayılan rol
}
```

----------

## 3) Infrastructure – Identity DbContext & Mapping



`**src/SBSaaS.Infrastructure/Persistence/SbsDbContext.cs**`  (ek tablo & index)

```
using SBSaaS.Domain.Entities.Invitations;

// ...
public DbSet<Invitation> Invitations => Set<Invitation>();

protected override void OnModelCreating(ModelBuilder b)
{
    base.OnModelCreating(b);

    b.Entity<Invitation>(e =>
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Email).HasMaxLength(256).IsRequired();
        e.Property(x => x.Token).HasMaxLength(128).IsRequired();
        e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique(false);
        e.HasIndex(x => x.Token).IsUnique();
    });
}
```

----------

## 4) JWT Ayarları, Token Servisi ve Options



`**src/SBSaaS.API/Auth/JwtOptions.cs**`

```
namespace SBSaaS.API.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public string SigningKey { get; set; } = default!; // uzun ve rastgele
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;
}
```

`**src/SBSaaS.API/Auth/TokenService.cs**`

```
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
```csharp

**Refresh token saklama**: Bu iş paketi kapsamında eklenen `RefreshToken` entity'si, kullanıcıya ait yenileme jetonlarını veritabanında güvenli bir şekilde saklamak için kullanılır. Jetonlar veritabanına hash'lenerek yazılmalı ve token rotasyonu (bir kez kullanıldıktan sonra eskisini iptal edip yenisini üretme) gibi güvenlik pratikleri uygulanmalıdır.
```

----------


## 5) Program.cs – Kimlik Doğrulama, Rate Limiting, Data Protection

`**src/SBSaaS.API/Program.cs**`  (özet parçalar)

```
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using SBSaaS.API.Auth;

var builder = WebApplication.CreateBuilder(args);

// JwtOptions
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<ITokenService, TokenService>();

// Identity (Infrastructure.AddInfrastructure ile eklenmiş olmalı)

// Authentication – JWT + Google + Microsoft
builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(o =>
{
    var cfg = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = cfg.Issuer,
        ValidAudience = cfg.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(cfg.SigningKey))
    };
})
.AddGoogle(opt =>
{
    opt.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
    opt.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
})
.AddMicrosoftAccount(opt =>
{
    opt.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"]!;
    opt.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"]!;
});

// Authorization – policy örnekleri
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantScoped", p => p.RequireClaim("tenant"));
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin","Owner"));
});

// Rate limiting (IP başına basit sabit pencere)
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("api", options =>
    {
        options.Window = TimeSpan.FromSeconds(1);
        options.PermitLimit = 20; // saniyede 20 istek
        options.QueueLimit = 0;
    });
});

// Data Protection – (opsiyonel Redis)
// builder.Services.AddDataProtection().PersistKeysToStackExchangeRedis(multiplexer, "dp-keys");

var app = builder.Build();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
```

----------

## 6) Auth Controller – Login/Refresh/Invite Akışı

`**src/SBSaaS.API/Controllers/AuthController.cs**`

```
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
```

> Not: Dış sağlayıcı (Google/Microsoft) için  `ExternalLogin`  uçları Aşağıda.

----------

## 7) External OAuth (Google/Microsoft) – Challenge & Callback

`**src/SBSaaS.API/Controllers/ExternalAuthController.cs**`

```
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SBSaaS.Domain.Entities;

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
```

> Üretimde: callback’te  `state`  doğrulama, tenant bağlama stratejisi ve redirect ile token verme (PKCE/CORS) gibi detayları ekleyin.

----------

## 8) Parola, Lockout, MFA ve Güvenlik Politikaları


`**Infrastructure.DependencyInjection**` **içindeki Identity ayarları**

```
services.AddIdentity<ApplicationUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 8;
    o.Password.RequireNonAlphanumeric = false;
    o.Password.RequireUppercase = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    o.User.RequireUniqueEmail = true;
    o.SignIn.RequireConfirmedEmail = false; // üretimde true önerilir
})
.AddEntityFrameworkStores<SbsDbContext>()
.AddDefaultTokenProviders();
```

**MFA**:  `TotpAuthenticatorTokenProvider`  ve e-posta/SMS doğrulama akışlarını ilerleyen adımlarda ekleyebilirsiniz.

----------

## 9) OpenAPI (G1) Güncellemesi



-   `/api/v1/auth/login`,  `/auth/refresh`,  `/auth/invite`,  `/auth/accept`
-   `/api/v1/auth/external/challenge/{provider}`,  `/auth/external/callback`
-   Security:  `bearerAuth`, zorunlu  `X-Tenant-Id`  (tenant bağlı uçlar).

----------

## 10) Testler


-   **Unit**: TokenService claim setleri, süreler; password/lockout kuralları.
-   **Integration**: Login → access/refresh al → refresh ile yenile; Invite → Accept; role-based endpoint’lere erişim.
-   **Security**: Cross-tenant erişim engeli (claim  `tenant`  ile API policy testleri); rate limit ihlali.

----------

## 11) Sonraki Paket

-   **A4 – Localization**: RequestLocalization + resource/DB destekli çeviri, kültür müzakeresi,  `tr-TR`  default; token içindeki kültür/zon tercihlerini UI/formatlama için kullanma.