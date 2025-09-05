using System.ComponentModel.DataAnnotations.Schema;

namespace SBSaaS.Domain.Entities.Metering;

[Table("external_sync_state", Schema = "metering")]
public class ExternalSyncState
{
    public string Provider { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string ExternalRef { get; set; } = string.Empty;
    public DateTimeOffset? LastSyncedAt { get; set; }
}
