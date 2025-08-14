Bu belge **C2 – OAuth Login (Google/Microsoft)** iş paketinin uçtan uca uygulamasını içerir. Hedef: Razor WebApp’te dış sağlayıcılarla giriş (cookie auth), güvenli challenge/callback akışı, tenant bağlama stratejileri ve (opsiyonel) API token exchange.

---

# 0) DoD – Definition of Done

- Cookie Authentication kuruldu; Google & Microsoft sağlayıcıları çalışır.
- `ExternalLogin` → `Callback` akışı başarılı; kullanıcı hesabı oluşturma/bağlama yapılıyor.
- `state` doğrulaması, `returnUrl` güvenliği (local URL) ve SameSite/HTTPS ayarları tamam.
- (Opsiyonel) Callback’te **API JWT token exchange** yapılıp cookie/Session’a yazılıyor.
- UI’da “Sign in with Google/Microsoft” butonları mevcut; Logout çalışır.

---

# 1) NuGet

```bash
 dotnet add src/SBSaaS.WebApp/SBSaaS.WebApp.csproj package Microsoft.AspNetCore.Authentication.Cookies
 dotnet add src/SBSaaS.WebApp/SBSaaS.WebApp.csproj package Microsoft.AspNetCore.Authentication.Google
 dotnet add src/SBSaaS.WebApp/SBSaaS.WebApp.csproj package Microsoft.AspNetCore.Authentication.MicrosoftAccount
```

---

# 2) appsettings – OAuth yapılandırmaları

``

```json
{
  "Authentication": {
    "Google": {
      "ClientId": "GOOGLE_CLIENT_ID",
      "ClientSecret": "GOOGLE_CLIENT_SECRET"
    },
    "Microsoft": {
      "ClientId": "MS_CLIENT_ID",
      "ClientSecret": "MS_CLIENT_SECRET"
    }
  },
  "Auth": {
    "CallbackPath": "/signin-oauth",
    "AllowedReturnHosts": ["localhost"],
    "ApiBaseUrl": "http://localhost:8080" // opsiyonel token exchange için
  }
}
```

> Gizlileri `dotnet user-secrets` ile ekleyin.

---

# 3) Program.cs – Cookie + External Providers

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = ctx => { if (ctx.Request.Path.StartsWithSegments("/api")) { ctx.Response.StatusCode = 401; return Task.CompletedTask; } ctx.Response.Redirect(ctx.RedirectUri); return Task.CompletedTask; }
    };
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
    options.SaveTokens = true;
})
.AddMicrosoftAccount(options =>
{
    options.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"]!;
    options.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"]!;
    options.SaveTokens = true;
});

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultControllerRoute();
app.Run();
```

> Üretimde **HTTPS zorunlu** (SameSite=None için `Secure` şart).

---

# 4) Account Controller – Challenge/Callback/Logout

``

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace SBSaaS.WebApp.Controllers;

public class AccountController : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl ?? Url.Content("~/");
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), new { returnUrl });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, provider);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null)
    {
        var info = await HttpContext.AuthenticateAsync("External"); // framework içi ad değişebilir; SaveTokens ile User.Claims’e de düşer
        var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var ext = await HttpContext.AuthenticateAsync(); // mevcut principal

        var principal = HttpContext.User; // provider principal
        var email = principal.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
            return RedirectToAction("Login", new { returnUrl, error = "Email scope required" });

        // Basit: provider claim’leri ile local cookie oluştur
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? email),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, principal.Identity?.Name ?? email)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        // Güvenli returnUrl (yalnızca yerel)
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }
}
```

> İleri aşamada kullanıcıyı **tenant** ile eşleyecek akış (invite/domain) ekleyeceğiz.

---

# 5) Login View – Provider Butonları

``

```razor
@{
    ViewData["Title"] = "Sign in";
}
<h2>Sign in</h2>
<form asp-action="ExternalLogin" method="post">
  <input type="hidden" name="returnUrl" value="@ViewData["ReturnUrl"]" />
  <button type="submit" name="provider" value="Google">Sign in with Google</button>
  <button type="submit" name="provider" value="Microsoft">Sign in with Microsoft</button>
</form>
```

---

# 6) (Opsiyonel) API Token Exchange

Callback’te e-postayı kullanarak API’dan JWT almak ve cookie’ye yazmak:

**Basit HttpClient**

```csharp
public class AuthApiClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;
    public AuthApiClient(HttpClient http, IConfiguration cfg) { _http = http; _cfg = cfg; }

    public async Task<string?> ExchangeAsync(string email)
    {
        var baseUrl = _cfg["Auth:ApiBaseUrl"] ?? "http://localhost:8080";
        var res = await _http.PostAsJsonAsync($"{baseUrl}/api/v1/auth/external/exchange", new { email });
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadFromJsonAsync<Dictionary<string,string>>();
        return json!.GetValueOrDefault("accessToken");
    }
}
```

**Kayıt**

```csharp
builder.Services.AddHttpClient<AuthApiClient>();
```

**Callback’te kullan** (örnek)

```csharp
var token = await _authApi.ExchangeAsync(email);
if (token != null)
{
    var claims = new List<Claim>{ new("api_token", token) };
    var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await HttpContext.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity[]{ (ClaimsIdentity)User.Identity!, id }));
}
```

---

# 7) Güvenlik Notları

- **SameSite & Secure**: Harici sağlayıcı dönüşlerinde Safari/iOS sorunları için cookie’ler `SameSite=None; Secure` olmalı. HTTPS zorunlu.
- **CORS/Redirect allowlist**: `returnUrl` yalnızca **local** olmalı (`Url.IsLocalUrl`).
- **Anti-forgery**: CSRF korunumu form postlarında `@Html.AntiForgeryToken()`; ExternalLogin formunda da ekleyin.
- **Scope**: En az `email` scope’u isteyin, aksi durumda kayıt için e-posta alınamaz.

---

# 8) Tenant Bağlama Stratejileri

- **Davet tokenı**: Kullanıcı linki ile gelir → callback’te davet doğrulanır → kullanıcı tenant’a bağlanır.
- **Domain eşleme**: `@firma.com` → `TenantId` eşlemesi (beyaz liste).
- **Kullanıcı seçimi**: Eğer birden fazla tenant ilişkisi varsa seçim ekranı.

---

# 9) Test Senaryoları

- Happy path: Google → callback → cookie set → Home.
- İptal/geri: Provider iptalinde Login ekranına hata mesajı.
- Hatalı/eksik scope: e-posta yoksa kayıt akışı reddedilir.
- `returnUrl` olarak harici URL verilirse engellenir.
- Microsoft ve Google her iki sağlayıcı ile giriş denemesi.

---

# 10) Sonraki Paket

- **C3 – Tenant & Yetkilendirme UI**: Menü görünürlüğü ve sayfa guard’ları (role/claim), tenant seçici; API çağrılarında `X-Tenant-Id` header yönetimi.

