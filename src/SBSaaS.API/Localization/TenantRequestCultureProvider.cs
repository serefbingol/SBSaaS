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
