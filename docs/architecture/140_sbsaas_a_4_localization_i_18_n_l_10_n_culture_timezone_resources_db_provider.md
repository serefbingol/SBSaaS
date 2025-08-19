
Bu belge  **A4 – Localization**  iş paketinin uçtan uca uygulamasını içerir.  `tr-TR`  varsayılanıyla çok dilli/çok kültürlü yapı, kültür müzakeresi (query/header/cookie/tenant), resource dosyaları +  **opsiyonel veritabanı tabanlı çeviri sağlayıcısı**, tarih/sayı/para birimi biçimlendirme yardımcıları, zaman dilimi yönetimi ve doğrulama/ModelState mesajlarının yerelleştirilmesi dahildir.

----------

## 0) DoD – Definition of Done


-   `RequestLocalization`  aktif;  **tr-TR**  varsayılan, en az  `tr-TR`,  `en-US`,  `de-DE`  desteklenir.
-   Kültür çözüm sırası:  **query → cookie → header → tenant default**  (fallback  `tr-TR`).
-   Resource tabanlı çeviriler (`Resources/*.resx`) çalışır; örnek controller yanıtları lokalize döner.
-   ModelState/`DataAnnotations`  hata mesajları yerelleşir.
-   (Opsiyonel)  **DB Localizer**  ile çeviriler veritabanından da okunabilir.
-   Zaman dilimi tercihleri tenant/kullanıcı seviyesinde uygulanır; sunumda biçimlendirme doğru çalışır.
-   G1/OpenAPI için  `Accept-Language`  ve  `X-Tenant-Id`  başlıkları belgelenir.

----------

## 1) NuGet Paketleri (kontrol)
```
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.Extensions.Localization
 dotnet add src/SBSaaS.API/SBSaaS.API.csproj package Microsoft.AspNetCore.Localization
```

----------

## 2) appsettings – Desteklenen kültürler

`**src/SBSaaS.API/appsettings.json**`

```
{
  "Localization": {
    "DefaultCulture": "tr-TR",
    "SupportedCultures": ["tr-TR", "en-US", "de-DE"]
  }
}
```

----------

## 3) Program.cs – RequestLocalization + MVC yerelleştirme

`**src/SBSaaS.API/Program.cs**`  (özet)

```
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;
using SBSaaS.API.Localization;

// services
var locCfg = builder.Configuration.GetSection("Localization");
var defaultCulture = locCfg["DefaultCulture"] ?? "tr-TR";
var supported = locCfg.GetSection("SupportedCultures").Get<string[]>() ?? new[]{"tr-TR","en-US","de-DE"};

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddControllers()
    .AddViewLocalization()               // Razor için gerekli (C paketinde kullanılacak)
    .AddDataAnnotationsLocalization();    // ModelState/DataAnnotations

builder.Services.AddScoped<TenantRequestCultureProvider>();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = supported.Select(c => new CultureInfo(c)).ToList();
    options.DefaultRequestCulture = new RequestCulture(defaultCulture);
    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;

    // Çözüm sırası: Query → Cookie → Header → Tenant
    options.RequestCultureProviders = new IRequestCultureProvider[]
    {
        new QueryStringRequestCultureProvider(),
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider(),
        // Tenant en sonda fallback gibi davranır
        new TenantRequestCultureProvider()
    };
});

var app = builder.Build();
app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
```

> **Not:**  `ResourcesPath = "Resources"`  ile API ve Razor projelerinde  `Resources/*.resx`  yapısını birlikte kullanabiliriz.

----------

## 4) TenantRequestCultureProvider (tenant/kullanıcı varsayılanları)

`**src/SBSaaS.API/Localization/TenantRequestCultureProvider.cs**`

```
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Primitives;
using SBSaaS.Application.Interfaces;
using System.Globalization;

namespace SBSaaS.API.Localization;

public class TenantRequestCultureProvider : RequestCultureProvider
{
    private readonly ITenantContext _tenant;
    private readonly IConfiguration _cfg;

    public TenantRequestCultureProvider(ITenantContext tenant, IConfiguration cfg)
    { _tenant = tenant; _cfg = cfg; }

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        // Tenant varsayılanını oku (ileri aşamada DB'den Tenant.Culture'a bakılabilir)
        var defaultCulture = _cfg["Localization:DefaultCulture"] ?? "tr-TR";
        if (_tenant.TenantId == Guid.Empty)
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(defaultCulture));

        // Burada Tenant tablosundan culture/timezone çekilebilir (cache ile).
        // Şimdilik config fallback kullanıyoruz.
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(defaultCulture));
    }
}
```

> İleride  `Tenant`  tablosuna  `Culture`/`UiCulture`/`TimeZone`  alanlarını ekleyip burada okuyarak varsayılanı belirleyin (A1’de alanlar mevcutsa doğrudan kullanın).

----------

## 5) Resource dosyaları – Shared strings

**Proje yapısı**

```
src/SBSaaS.API/Resources/
  Shared.resx           # varsayılan: tr-TR
  Shared.en-US.resx
  Shared.de-DE.resx
```

**Anahtar örnekleri**

-   `Hello`  → (tr) "Merhaba" / (en) "Hello" / (de) "Hallo"
-   `Unauthorized`  → (tr) "Yetkisiz erişim" / (en) "Unauthorized" / (de) "Nicht autorisiert"

**Kullanım – Controller**  
`**src/SBSaaS.API/Controllers/SampleController.cs**`

```
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class SampleController : ControllerBase
{
    private readonly IStringLocalizer _loc;
    public SampleController(IStringLocalizerFactory factory)
    {
        var asm = typeof(Program).Assembly.GetName().Name!;
        _loc = factory.Create("Shared", asm);
    }

    [HttpGet("hello")]
    public IActionResult Hello() => Ok(new { message = _loc["Hello"] });
}
```

----------

## 6) ModelState/Validation mesajlarının yerelleştirilmesi

`**Program.cs**`  içinde  `AddDataAnnotationsLocalization()`  zaten eklendi.  
`**Resources/Shared.resx**`  içine yaygın mesajları koyun (örn.  `The {0} field is required.`  karşılıkları).  
FluentValidation kullanıyorsanız kültür tabanlı mesajlar için  `ValidatorOptions.Global.LanguageManager.Culture = ...`  ayarlayabilirsiniz.

----------

## 7) Zaman Dilimi (TimeZone) ve Biçimlendirme Yardımcıları

UTC saklama, kullanıcı/tenant zaman dilimine göre  **sunumda**  dönüştürme önerilir.

`**src/SBSaaS.API/Localization/FormatService.cs**`

```
using System.Globalization;

namespace SBSaaS.API.Localization;

public interface IFormatService
{
    string Date(DateTimeOffset utc, string? timeZoneId = null, string? culture = null);
    string Currency(decimal amount, string currencyCode = "TRY", string? culture = null);
    string Number(double value, string? culture = null);
}

public class FormatService : IFormatService
{
    public string Date(DateTimeOffset utc, string? timeZoneId = null, string? culture = null)
    {
        var tz = !string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.FindSystemTimeZoneById(timeZoneId) : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(utc, tz);
        var ci = new CultureInfo(culture ?? CultureInfo.CurrentCulture.Name);
        return local.ToString(ci);
    }

    public string Currency(decimal amount, string currencyCode = "TRY", string? culture = null)
    {
        var ci = new CultureInfo(culture ?? CultureInfo.CurrentCulture.Name);
        var format = string.Create(ci, $"{{0:C}}", amount);
        // CurrencySymbol hedef kültüre göre değişir; sabit kodlu kod istiyorsanız string interpolasyonla ekleyin
        return format;
    }

    public string Number(double value, string? culture = null)
    {
        var ci = new CultureInfo(culture ?? CultureInfo.CurrentCulture.Name);
        return value.ToString("N", ci);
    }
}
```

**DI kaydı**

```
builder.Services.AddSingleton<IFormatService, FormatService>();
```

----------

## 8) DB Tabanlı Localizer (opsiyonel)

Veritabanından çeviri çekmek için basit bir  `IStringLocalizer`  implementasyonu:

**Tablo**

```
CREATE TABLE IF NOT EXISTS i18n_translations (
  id bigserial PRIMARY KEY,
  key text NOT NULL,
  culture text NOT NULL,
  value text NOT NULL,
  UNIQUE(key, culture)
);
```

**Entity & DbContext map**  
`**src/SBSaaS.Infrastructure/Localization/Translation.cs**`

```
namespace SBSaaS.Infrastructure.Localization;
public class Translation { public long Id { get; set; } public string Key { get; set; } = default!; public string Culture { get; set; } = default!; public string Value { get; set; } = default!; }
```

`**SbsDbContext.OnModelCreating**`

```
builder.Entity<SBSaaS.Infrastructure.Localization.Translation>(b =>
{
    b.ToTable("i18n_translations");
    b.HasKey(x => x.Id);
    b.HasIndex(x => new { x.Key, x.Culture }).IsUnique();
});
```

**Localizer**  
`**src/SBSaaS.Infrastructure/Localization/DbStringLocalizer.cs**`

```
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;

namespace SBSaaS.Infrastructure.Localization;

public class DbStringLocalizer : IStringLocalizer
{
    private readonly SbsDbContext _db;
    private readonly string _baseName;

    public DbStringLocalizer(SbsDbContext db, string baseName)
    { _db = db; _baseName = baseName; }

    public LocalizedString this[string name]
    {
        get
        {
            var culture = Thread.CurrentThread.CurrentUICulture.Name;
            var value = _db.Set<Translation>().AsNoTracking()
                .Where(x => x.Key == name && x.Culture == culture)
                .Select(x => x.Value)
                .FirstOrDefault();
            var notFound = value is null;
            value ??= name; // fallback
            return new LocalizedString(name, value, notFound);
        }
    }

    public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
    public IStringLocalizer WithCulture(CultureInfo culture) => this;
}
```

**Factory**  
`**src/SBSaaS.Infrastructure/Localization/DbStringLocalizerFactory.cs**`

```
using Microsoft.Extensions.Localization;

namespace SBSaaS.Infrastructure.Localization;

public class DbStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly SbsDbContext _db;
    public DbStringLocalizerFactory(SbsDbContext db) => _db = db;
    public IStringLocalizer Create(Type resourceSource) => new DbStringLocalizer(_db, resourceSource.FullName!);
    public IStringLocalizer Create(string baseName, string location) => new DbStringLocalizer(_db, baseName);
}
```

**Kayıt (opsiyonel olarak Resource yerine DB kullan)**  
`**Program.cs**`

```
// builder.Services.AddSingleton<IStringLocalizerFactory, DbStringLocalizerFactory>();
```

> Performans için çevirileri memory cache ile tutmanız önerilir (kültür + key bazlı).

----------

## 9) OpenAPI (G1) – Belgeleme Notları

-   `Accept-Language`  kullanımı ve desteklenen kültür listesi.
-   Tenant varsayılan kültürü (eğer ayarlanmışsa) ve kültür çakışma kuralları.
-   Tarih/sayı/para birimi biçimlendirmesinin  **istemci sorumluluğu**  vs  **sunucu sorumluluğu**  ayrımı (bu projede API, bazı metinleri lokalize edebilir; tarih/sayı çoğunlukla ham değer + UI formatı önerilir).

----------

## 10) Testler

-   `GET /api/v1/sample/hello`  çağrısını  `?culture=en-US`  ve  `Accept-Language: de-DE`  ile deneyin.
-   ModelState hata mesajlarının dil değiştirmesi (zorunlu alan testleri).
-   Culture cookie kalıcılığı:  `CookieRequestCultureProvider.DefaultCookieName`  kontrolü.
-   Tenant varsayılan kültürüne düşüş testi.

----------

## 11) Sonraki Paket

-   **A5 – MinIO Entegrasyonu**: presigned URL, MIME doğrulama, tenant bazlı bucket/prefix stratejisi, dosya boyutu limitleri ve örnek upload/download akışları.