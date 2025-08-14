namespace SBSaaS.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Culture { get; set; } // örn: "tr-TR"
    public string? UiCulture { get; set; }
    public string? TimeZone { get; set; } // IANA/Windows TZ

    public static Tenant From(string v)
    {
        throw new NotImplementedException();
    }
}