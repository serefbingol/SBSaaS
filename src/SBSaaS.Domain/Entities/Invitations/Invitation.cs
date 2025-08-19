namespace SBSaaS.Domain.Entities.Invitations;

public class Invitation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = default!;
    public string Token { get; set; } = default!;
    public DateTimeOffset ExpiresUtc { get; set; }
    public bool Accepted { get; set; }
    public string? Role { get; set; }
}
