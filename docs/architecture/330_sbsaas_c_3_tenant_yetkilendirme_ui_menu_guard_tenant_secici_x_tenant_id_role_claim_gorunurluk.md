Bu belge **C3 – Tenant & Yetkilendirme UI** iş paketinin uçtan uca uygulamasını içerir. Hedef: Razor WebApp’te **tenant seçimi**, **rol/claim bazlı görünürlük**, API çağrılarında otomatik `` ekleme ve sayfa/menü korumaları.

---

# 0) DoD – Definition of Done

- Kullanıcı **aktif tenant** seçebiliyor; seçim **cookie**/**session**’da saklanıyor.
- API’ye giden tüm isteklerde `` header otomatik ekleniyor.
- Menü ve sayfa erişimleri **rol/claim** bazlı gizleniyor/gösteriliyor (UI + Server).
- `Authorize` attribute’ları ve **policy**’ler kullanılıyor; **View** tarafında `AuthorizeTagHelper` etkin.
- Tenant yoksa korunan sayfalara erişimde **TenantRequired** uyarısı/redirect akışı var.

---

# 1) NuGet (WebApp)

```bash
 dotnet add src/SBSaaS.WebApp/SBSaaS.WebApp.csproj package Microsoft.AspNetCore.Authorization
 dotnet add src/SBSaaS.WebApp/SBSaaS.WebApp.csproj package Microsoft.Extensions.Http
```

---

# 2) Aktif Tenant Saklama (Cookie + Session)

**Saklama stratejisi**

- Cookie adı: `sbsaas_tenant`
- Değer: `Guid` (string)
- Süre: 30 gün

``

```csharp
namespace SBSaaS.WebApp.Services;

public interface ITenantSelectionService
{
    Guid? GetActiveTenant(HttpContext http);
    void SetActiveTenant(HttpContext http, Guid tenantId);
}

public class TenantSelectionService : ITenantSelectionService
{
    private const string CookieName = "sbsaas_tenant";
    public Guid? GetActiveTenant(HttpContext http)
        => Guid.TryParse(http.Request.Cookies[CookieName], out var id) ? id : null;

    public void SetActiveTenant(HttpContext http, Guid tenantId)
        => http.Response.Cookies.Append(CookieName, tenantId.ToString(), new CookieOptions
        {
            IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
}
```

**DI** (`Program.cs`)

```csharp
builder.Services.AddSingleton<ITenantSelectionService, TenantSelectionService>();
```

---

# 3) API HttpClient – X-Tenant-Id Enjeksiyonu

**DelegatingHandler** `X-Tenant-Id` ekler:

``

```csharp
using System.Net.Http.Headers;
using System.Net.Http;
using SBSaaS.WebApp.Services;

public class XTenantHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _http;
    private readonly ITenantSelectionService _sel;
    public XTenantHandler(IHttpContextAccessor http, ITenantSelectionService sel)
    { _http = http; _sel = sel; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tid = _http.HttpContext;
        if (httpContext is not null)
        {
            var tid = _sel.GetActiveTenant(httpContext);
            if (tid is Guid g)
            {
                request.Headers.TryAddWithoutValidation("X-Tenant-Id", g.ToString());
            }
        }
            return base.SendAsync(request, cancellationToken);
    }
}
```

**Kayıt** (`Program.cs`)

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<XTenantHandler>();

builder.Services.AddHttpClient("ApiClient", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:8080/");
})
.AddHttpMessageHandler<XTenantHandler>();
```

> Gerekiyorsa JWT taşıyorsanız bir **BearerTokenHandler** ile `Authorization: Bearer` da ekleyin.

---

# 4) Tenant Seçici UI

Kullanıcının erişebildiği tenant listesi API’den çekilir (`/tenants`).

``

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using SBSaaS.WebApp.Services;

[Authorize]
public class TenantController : Controller
{
    private readonly ITenantSelectionService _sel; private readonly IHttpClientFactory _http;
    public TenantController(ITenantSelectionService sel, IHttpClientFactory http) { _sel = sel; _http = http; }

    [HttpGet]
    public async Task<IActionResult> Switch()
    {
        var client = _http.CreateClient("ApiClient");
        var list = await client.GetFromJsonAsync<List<TenantDto>>("api/v1/tenants");
        var active = _sel.GetActiveTenant(HttpContext);
        return View(new SwitchVm(list ?? new(), active));
    }

    [HttpPost]
    public IActionResult Set(Guid tenantId, string? returnUrl = "/")
    {
        _sel.SetActiveTenant(HttpContext, tenantId);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}

public record TenantDto(Guid Id, string Name);
public record SwitchVm(List<TenantDto> Tenants, Guid? ActiveTenant);
```

``

```razor
@model SwitchVm
<h2>Tenant Seç</h2>
<form asp-action="Set" method="post">
  <select name="tenantId">
    @foreach (var t in Model.Tenants)
    {
      <option value="@t.Id" selected="@(Model.ActiveTenant == t.Id)">@t.Name</option>
    }
  </select>
  <input type="hidden" name="returnUrl" value="@Context.Request.Headers["Referer"].ToString()" />
  <button type="submit">Aktifleştir</button>
</form>
```

**Header’a mini seçici (opsiyonel)** – `_Layout.cshtml` içine kısayol linki:

```razor
<a asp-controller="Tenant" asp-action="Switch">@("Tenant: " + (Context.Request.Cookies["sbsaas_tenant"] ?? "—"))</a>
```

---

# 5) Menü/Sayfa Yetki Koruması (UI + Server)

**Sunucu tarafı**

- Controller/action’larda `[Authorize(Roles = "Admin,Owner")]` vb.
- Policy tanımı: `AdminOnly`, `CanManageFiles` gibi policies.

`` (özet)

```csharp
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("AdminOnly", p => p.RequireRole("Admin","Owner"));
    o.AddPolicy("CanManageFiles", p => p.RequireClaim("perm:files:write"));
});
```

**View tarafı** – Authorize TagHelper aktif etmek için `_ViewImports.cshtml`:

```razor
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
@addTagHelper *, Microsoft.AspNetCore.Authorization
```

**Kullanım – Menü öğesi gizleme**

```razor
<authorize roles="Admin,Owner">
  <li><a asp-controller="Admin" asp-action="Index">Yönetim</a></li>
</authorize>
```

**Claim bazlı**

```razor
<authorize policy="CanManageFiles">
  <li><a asp-controller="Files" asp-action="Index">Dosyalar</a></li>
</authorize>
```

---

# 6) TenantRequired Filter (korunan sayfalar için)

Aktif tenant olmadan belirli sayfalara erişimi engelle:

``

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SBSaaS.WebApp.Services;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireTenantAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var sel = context.HttpContext.RequestServices.GetRequiredService<ITenantSelectionService>();
        if (sel.GetActiveTenant(context.HttpContext) is null)
            context.Result = new RedirectToActionResult("Switch", "Tenant", null);
    }
}
```

**Kullanım**

```csharp
[Authorize]
[RequireTenant]
public class ProjectsController : Controller { /* ... */ }
```

---

# 7) Menü Modeli (Server-driven Navigation)

Rol/claim’e göre menü öğelerini sunucu tarafında üretmek isterseniz basit bir model:

``

```csharp
public class NavItem
{
    public string Text { get; set; } = default!;
    public string Action { get; set; } = default!;
    public string Controller { get; set; } = default!;
    public string? Policy { get; set; }
    public string? RolesCsv { get; set; }
}
```

``

```csharp
public class NavService
{
    private readonly IAuthorizationService _auth; private readonly IHttpContextAccessor _http;
    public NavService(IAuthorizationService auth, IHttpContextAccessor http) { _auth = auth; _http = http; }

    public async Task<List<NavItem>> VisibleAsync(IEnumerable<NavItem> all)
    {
        var user = _http.HttpContext!.User;
        var list = new List<NavItem>();
        foreach (var i in all)
        {
            var ok = true;
            if (!string.IsNullOrEmpty(i.RolesCsv))
                ok &= i.RolesCsv.Split(',').Any(r => user.IsInRole(r.Trim()));
            if (ok && !string.IsNullOrEmpty(i.Policy))
                ok &= (await _auth.AuthorizeAsync(user, null, i.Policy)).Succeeded;
            if (ok) list.Add(i);
        }
        return list;
    }
}
```

Layout’ta `VisibleAsync` sonucu ile nav render edilebilir.

---

# 8) Hata Mesajları & i18n

C1/C2 ile uyumlu olacak şekilde uyarıları lokalize edin:

- "Tenant seçiniz" / "You must select a tenant" gibi metinler `Shared.resx` içinde.
- Yetkisiz erişim: AccessDenied view’ında kültür bazlı mesaj.

---

# 9) Test Senaryoları

- **Tenant seçimi**: Set → cookie yazıldı mı? API çağrılarında header geliyor mu?
- **Menü görünürlüğü**: Rol/claim’e göre öğeler gizleniyor mu? (UI + server)
- **RequireTenant**: Aktif tenant yokken korunan sayfalar Switch’e yönlendiriyor mu?
- **Çok tenant**: Tenant değişiminde UI verileri ve listelemeler beklendiği gibi filtreleniyor mu?
- **XSRF**: Tenant set formunda anti-forgery aktif mi?

---

# 10) Sonraki Paket

- **D1 – CI/CD Pipeline**: Build/test, migration doğrulama, container build & push; ardından **D2 – Logging & Monitoring** (Serilog + OTel + Grafana).

