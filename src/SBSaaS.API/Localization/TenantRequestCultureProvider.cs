using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Primitives;
using SBSaaS.Application.Interfaces;
using System.Globalization;

namespace SBSaaS.API.Localization;

// Örnek: /src/SBSaaS.API/Localization/TenantRequestCultureProvider.cs dosyasında yapılması gereken değişiklik
public class TenantRequestCultureProvider : RequestCultureProvider
{
    // Constructor'daki scoped bağımlılıkları kaldırın.
    public TenantRequestCultureProvider() { }

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        // Scoped servisleri doğrudan HttpContext üzerinden çözümleyin.
        var tenantContext = httpContext.RequestServices.GetRequiredService<ITenantContext>();
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();

        var defaultCulture = config["Localization:DefaultCulture"] ?? "tr-TR";
        if (tenantContext.TenantId == Guid.Empty)
        {
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(defaultCulture));
        }

        // Gelecekte veritabanından tenant'a özel culture burada okunacak.
        // var tenantCulture = ...
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(defaultCulture));
    }
}

