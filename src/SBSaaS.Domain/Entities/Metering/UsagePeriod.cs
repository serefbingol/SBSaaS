using System.ComponentModel.DataAnnotations.Schema;

namespace SBSaaS.Domain.Entities.Metering;

[Table("usage_period", Schema = "metering")]
public class UsagePeriod
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Quantity { get; set; }
    public bool Closed { get; set; }
}
