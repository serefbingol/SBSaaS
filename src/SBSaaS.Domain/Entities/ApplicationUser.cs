using Microsoft.AspNetCore.Identity;

namespace SBSaaS.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public Guid TenantId { get; set; }
    public string? DisplayName { get; set; }
}
