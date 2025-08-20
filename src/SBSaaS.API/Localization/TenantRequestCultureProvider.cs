using Microsoft.AspNetCore.Localization;
using SBSaaS.Application.Interfaces;
using SBSaaS.Infrastructure.Persistence;
using System.Globalization;

namespace SBSaaS.API.Localization;

public class TenantRequestCultureProvider : RequestCultureProvider
{
    private readonly IConfiguration _cfg;

    public TenantRequestCultureProvider(IConfiguration cfg) => _cfg = cfg;

    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        // Scoped servisleri HttpContext üzerinden çözümle. Bu, provider'ın kendisinin
        // singleton ömrüne sahip olmasına rağmen istek bazlı servislere erişmesini sağlar.
        var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();
        var dbContext = httpContext.RequestServices.GetService<SbsDbContext>();

        if (tenantContext is null || dbContext is null)
        {
            // Servisler çözümlenemezse, varsayılan kültürü döndür.
            return new ProviderCultureResult(_cfg["Localization:DefaultCulture"] ?? "tr-TR");
        }

        // Tenant bağlamı yoksa veya TenantId boşsa, varsayılan kültürü kullan.
        if (tenantContext.TenantId == Guid.Empty)
        {
            return new ProviderCultureResult(_cfg["Localization:DefaultCulture"] ?? "tr-TR");
        }

        // Tenant'ın UI kültür bilgisini veritabanından al. Performans için cache eklenebilir.
        var tenant = await dbContext.Tenants.FindAsync(new object[] { tenantContext.TenantId }, httpContext.RequestAborted);
        var culture = tenant?.UiCulture;

        // Eğer Tenant bulunamazsa veya UiCulture tanımlı değilse, config'deki varsayılanı kullan.
        return new ProviderCultureResult(culture ?? _cfg["Localization:DefaultCulture"] ?? "tr-TR");
    }
}
