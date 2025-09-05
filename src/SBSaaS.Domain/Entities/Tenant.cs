using SBSaaS.Domain.Common;

namespace SBSaaS.Domain.Entities;

public class Tenant : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Veritabanı başına kiracı senaryoları için, şimdilik opsiyonel.
    //public string? ConnectionString { get; set; }

    // A4 - Localization dokümanından gelen ayarlar
    public string? Culture { get; set; }
    public string? UiCulture { get; set; }
    public string? TimeZone { get; set; }
}

