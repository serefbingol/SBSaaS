Bu belge **C1 – Razor i18n** iş paketinin uçtan uca uygulamasını içerir. Amaç: WebApp (Razor Pages/MVC) tarafında çok dilli/çok kültürlü UI; `tr-TR` varsayılan; dil seçici; DataAnnotations & View yerelleştirme; tarih/sayı/para biçimlendirme.

---

# 0) DoD – Definition of Done

- `AddLocalization` + `AddViewLocalization` + `AddDataAnnotationsLocalization` yapılandırıldı.
- Varsayılan kültür **tr-TR**, desteklenenler en az **en-US**, **de-DE**.
- Dil seçici (dropdown) cookie yazar; query/header de desteklenir.
- View/Partial/Component için `.resx` dosyaları çalışır (Shared + View bazlı).
- ModelState/Doğrulama mesajları lokalize.
- (Opsiyonel) route tabanlı kültür `/{culture}/...` çalışır.

---

# 1) NuGet

```bash
 dotnet add src/SBSaaS.WebApp/SBSaaS.WebApp.csproj package Microsoft.Extensions.Localization
```

---

# 2) Klasör Düzeni (öneri)

```
src/SBSaaS.WebApp/
  Resources/
    Shared.resx
    Shared.en-US.resx
    Shared.de-DE.resx
    Views/
      Home/Index.resx
      Home/Index.en-US.resx
      Home/Index.de-DE.resx
  Controllers/
    CultureController.cs
  Views/
    Shared/_Layout.cshtml
    Shared/_CultureSelector.cshtml
    Home/Index.cshtml
```

---

# 3) Program.cs – Localization Kurulumu

```csharp
using Microsoft.AspNetCore.Localization;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

var supported = new[] { "tr-TR", "en-US", "de-DE" };
var cultures = supported.Select(c => new CultureInfo(c)).ToList();

builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture("tr-TR");
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
    o.RequestCultureProviders = new IRequestCultureProvider[]
    {
        new QueryStringRequestCultureProvider(),
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider()
    };
});

var app = builder.Build();

app.UseRequestLocalization(app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value);
app.UseStaticFiles();
app.UseRouting();
app.MapDefaultControllerRoute();
app.Run();
```

> API projesindeki A4 paketinde tanımlı kültür listesiyle senkron gidin.

---

# 4) CultureController – Dil Seçimi (Cookie)

``

```csharp
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace SBSaaS.WebApp.Controllers;

public class CultureController : Controller
{
    [HttpPost]
    public IActionResult Set(string culture, string returnUrl = "/")
    {
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
        );
        return LocalRedirect(returnUrl);
    }
}
```

---

# 5) Dil Seçici Partial & Layout Entegrasyonu

``

```razor
@{
    var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
}
<form asp-controller="Culture" asp-action="Set" method="post" class="inline">
  <select name="culture" onchange="this.form.submit()">
    <option value="tr-TR" selected="@(culture=="tr-TR")">Türkçe</option>
    <option value="en-US" selected="@(culture=="en-US")">English</option>
    <option value="de-DE" selected="@(culture=="de-DE")">Deutsch</option>
  </select>
  <input type="hidden" name="returnUrl" value="@Context.Request.Path + Context.Request.QueryString"/>
</form>
```

``

```razor
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>@ViewData["Title"] - SBSaaS</title>
</head>
<body>
    <header>
        @await Html.PartialAsync("_CultureSelector")
    </header>
    <main class="container">
        @RenderBody()
    </main>
</body>
</html>
```

---

# 6) View Yerelleştirme Örneği

**Shared kaynaklar**\
`Resources/Shared.resx` içinde `Welcome` anahtarı: tr: "Merhaba", en: "Hello", de: "Hallo".

**View** – `Views/Home/Index.cshtml`

```razor
@using System.Globalization
@inject Microsoft.AspNetCore.Mvc.Localization.IViewLocalizer L
@{
    ViewData["Title"] = L["Welcome"];
}
<h1>@L["Welcome"]</h1>
<p>@string.Format(CultureInfo.CurrentCulture, "{0:C}", 1234.56m)</p>
```

**View-bazlı kaynak**\
`Resources/Views/Home/Index.resx` içine `PageTitle` ekleyin ve view’de `L["PageTitle"]` olarak tüketin.

---

# 7) DataAnnotations & ModelState Yerelleştirme

**Model**

```csharp
using System.ComponentModel.DataAnnotations;

public class RegisterVm
{
    [Required(ErrorMessage = "FieldRequired")]
    [EmailAddress(ErrorMessage = "EmailInvalid")]
    public string Email { get; set; } = string.Empty;
}
```

**Resources/Shared.resx**

```
FieldRequired = Bu alan zorunludur.
EmailInvalid = Geçerli bir e-posta girin.
```

> `AddDataAnnotationsLocalization` ile bu anahtarlar otomatik çözülür.

---

# 8) (Opsiyonel) Route Tabanlı Kültür

**Program.cs**

```csharp
app.MapControllerRoute(
    name: "default-cultured",
    pattern: "{culture:regex(^[a-z]{{2}}-[A-Z]{{2}}$)}/{controller=Home}/{action=Index}/{id?}");
```

Link üretiminde `asp-route-culture` kullanın. Cookie ve query ile birlikte çalışabilir.

---

# 9) Biçimlendirme Yardımcıları

WebApp projesinin kendi içinde tanımlanan `IFormatService`'i UI'da kullanabilirsiniz:

```razor
@inject SBSaaS.WebApp.Services.IFormatService F
<span>@F.Currency(199.99m, "TRY")</span>
```

> Alternatif: WebApp içinde hafif bir `FormatService` tanımlayın.

---

# 10) Test Senaryoları

- Dil seçimi sonrası sayfa tekrar yüklenince metinler değişmeli.
- `en-US` için para birimi sembolü ve tarih biçimi farklı görünmeli.
- DataAnnotations mesajları kültüre göre değişmeli.
- Route tabanlı kültür aktifse linkler `/{culture}/...` formatında çalışmalı.

---

# 11) Sonraki Paket

- **C2 – OAuth Login (Google/Microsoft)**: Cookie auth + dış sağlayıcı butonları, callback akışı; WebApp’in API ile token alışverişi veya cookie oturum modeli.
