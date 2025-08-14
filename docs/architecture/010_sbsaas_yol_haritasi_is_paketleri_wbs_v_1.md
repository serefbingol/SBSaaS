Bu belge **C1 – Razor i18n** iş paketinin uçtan uca uygulanabilir kılavuzudur. Amaç, WebApp (Razor Pages/MVC) tarafında çok dilli, çok kültürlü ve formatlama destekli bir UI sağlamaktır.

---

# 0) DoD – Definition of Done
- UI tarafında `IViewLocalizer` ve `IStringLocalizer` entegre edildi.
- Varsayılan kültür `tr-TR`, desteklenen kültürler (`en-US`, `de-DE`) ile birlikte çalışıyor.
- Dil seçim mekanizması (dropdown/select) ve kültür cookie’si mevcut.
- Tarih, sayı, para birimi formatlaması view katmanında kültüre göre yapılıyor.
- Resource (.resx) dosyaları organize ve modüler.
- Testler: farklı kültürlerle UI doğrulaması.

---

# 1) NuGet Paketleri
```bash
 dotnet add src/SBSaaS.WebApp/SBSaaS.WebApp.csproj package Microsoft.Extensions.Localization
```

---

# 2) Resource Dosyaları
`Resources/Views` klasöründe view bazlı `.resx` dosyaları:
- `Index.cshtml.tr.resx`
- `Index.cshtml.en.resx`
- `Index.cshtml.de.resx`

**Örnek:**
```xml
<data name="WelcomeMessage" xml:space="preserve">
  <value>Hoş geldiniz</value>
</data>
```

---

# 3) Startup & Middleware
**`Program.cs`**:
```csharp
var supportedCultures = new[] { "tr-TR", "en-US", "de-DE" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("tr-TR")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);
```

---

# 4) View Kullanımı
**`Index.cshtml`**:
```razor
@inject IViewLocalizer Localizer
<h1>@Localizer["WelcomeMessage"]</h1>
<p>@string.Format(CultureInfo.CurrentCulture, "{0:C}", 1234.56m)</p>
```

---

# 5) Dil Seçici (UI Component)
**`_CultureSelectorPartial.cshtml`**:
```razor
<form method="post" asp-controller="Culture" asp-action="Set">
  <select name="culture" onchange="this.form.submit();">
    <option value="tr-TR">Türkçe</option>
    <option value="en-US">English</option>
    <option value="de-DE">Deutsch</option>
  </select>
</form>
```
**Controller:** kültürü cookie’ye yazar ve redirect yapar.

---

# 6) Formatlama Yardımcıları
UI tarafında **HtmlHelper** veya **TagHelper** ile tarih, sayı ve para formatlama.

---

# 7) Testler
- UI dil değişimi sonrası textlerin doğru görünmesi.
- Farklı kültürlerde tarih/para formatlarının doğrulanması.
- Cookie/Accept-Language başlık öncelik testi.

---

# 8) Sonraki Paket
- **C2 – OAuth Login**: Google/Microsoft ile giriş ve cookie authentication entegrasyonu.

