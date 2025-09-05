using SBSaaS.Domain.Common;
using System;

namespace SBSaaS.Domain.Entities.Projects;

public class Project : BaseAuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = default!;   // unique in tenant
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}
