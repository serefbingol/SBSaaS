Bu belge **A4 – Localization** iş paketinin uçtan uca uygulanabilir kılavuzudur. Çok dilli, çok kültürlü ve çok formatlı yapı için gerekli altyapı, varsayılan `tr-TR` kültürü, kaynak dosyalar, kültür müzakeresi, veri formatlama ve test stratejilerini içerir.

---

# 0) DoD – Definition of Done
- API ve WebApp tarafında `RequestLocalization` pipeline'a entegre edildi.
- Varsayılan kültür `tr-TR` ve fallback stratejisi çalışıyor.
- Resource (.resx) ve/veya DB tabanlı çeviri altyapısı kuruldu.
- Tarih, sayı, para birimi formatlama `CultureInfo` üzerinden merkezi yönetiliyor.
- Kullanıcı tercihleri (dil, kültür) profil veya token ile taşınıyor.
- OpenAPI şeması (G1) ilgili header/query parametreleri ile güncellendi.

---

# 1) NuGet Paketleri
```bash
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.Extensions.Localization
 dotnet add src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj package Microsoft.Extensions.Localization
```

---

# 2) Resource Dosyaları
`Resources` klasörü altında:
- `SharedResources.tr.resx` (varsayılan)
- `SharedResources.en.resx`
- `SharedResources.de.resx`

**Örnek anahtarlar**:
```xml
<data name="Welcome" xml:space="preserve">
  <value>Hoş geldiniz</value>
</data>
```

---

# 3) RequestLocalization Middleware
**`Program.cs`**:
```csharp
var supportedCultures = new[] { "tr-TR", "en-US", "de-DE" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("tr-TR")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

localizationOptions.RequestCultureProviders.Insert(0, new AcceptLanguageHeaderRequestCultureProvider());

app.UseRequestLocalization(localizationOptions);
```

---

# 4) DB Destekli Çeviri (Opsiyonel)
`Translation` tablosu:
```csharp
public class Translation
{
    public Guid Id { get; set; }
    public string Key { get; set; } = default!;
    public string Culture { get; set; } = default!;
    public string Value { get; set; } = default!;
}
```

`IStringLocalizer` implementasyonu ile DB fallback → Resource dosyası zinciri kurulabilir.

---

# 5) Formatlama Yardımcıları
**`FormattingService`**:
```csharp
public class FormattingService
{
    public string FormatCurrency(decimal amount, CultureInfo culture)
        => string.Format(culture, "{0:C}", amount);
    public string FormatDate(DateTime date, CultureInfo culture)
        => date.ToString(culture.DateTimeFormat.ShortDatePattern, culture);
}
```

---

# 6) Kullanıcı Tercihleri
- Kullanıcı profiline `PreferredCulture` alanı eklenir.
- JWT claim olarak `culture` eklenebilir.
- İsteklerde `Accept-Language` veya `X-Culture` header öncelikli okunur.

---

# 7) OpenAPI Güncellemesi
- Tüm endpoint’lere `Accept-Language` header parametresi.
- Örnek yanıtlar dil bazlı gösterilir.

---

# 8) Testler
- Varsayılan kültür ve fallback testi.
- Farklı kültürlerde tarih/para format doğrulaması.
- DB → Resource fallback zinciri testi.

---

# 9) Sonraki Paket
- **A5 – MinIO Entegrasyonu**: Dosya yükleme, presigned URL, ACL yönetimi ve tenant bazlı dosya izolasyonu.

