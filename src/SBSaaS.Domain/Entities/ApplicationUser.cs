using Microsoft.AspNetCore.Identity;

namespace SBSaaS.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    // ...existing code...

    public string? ProfilePhotoUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid TenantId { get; set; }

    // Tenant ilişkisi için navigation property
    public virtual Tenant? Tenant { get; set; }
}
// ... other using directives
