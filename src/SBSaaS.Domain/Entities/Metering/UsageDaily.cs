using System.ComponentModel.DataAnnotations.Schema;

namespace SBSaaS.Domain.Entities.Metering;

[Table("usage_daily", Schema = "metering")]
public class UsageDaily
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateOnly Day { get; set; }
    public decimal Quantity { get; set; }
}
