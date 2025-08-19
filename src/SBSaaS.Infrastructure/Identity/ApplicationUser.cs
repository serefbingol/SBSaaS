using Microsoft.AspNetCore.Identity;

namespace SBSaaS.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public Guid TenantId { get; set; }
    public string? DisplayName { get; set; }
}