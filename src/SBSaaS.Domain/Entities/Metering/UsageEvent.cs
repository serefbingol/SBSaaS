using System.ComponentModel.DataAnnotations.Schema;

namespace SBSaaS.Domain.Entities.Metering;

[Table("usage_event", Schema = "metering")]
public class UsageEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
