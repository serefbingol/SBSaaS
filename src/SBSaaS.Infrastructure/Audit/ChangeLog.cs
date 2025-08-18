namespace SBSaaS.Infrastructure.Audit;

public class ChangeLog
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public string TableName { get; set; } = default!;
    public string KeyValues { get; set; } = default!;   // JSON
    public string? OldValues { get; set; }              // JSON
    public string? NewValues { get; set; }              // JSON
    public string Operation { get; set; } = default!;   // INSERT/UPDATE/DELETE
    public string? UserId { get; set; }
    public DateTimeOffset UtcDate { get; set; }
}