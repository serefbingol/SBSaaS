using SBSaaS.Domain.Common;
using System;

namespace SBSaaS.Domain.Entities.Projects;

public class Project : AuditableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = default!;   // unique in tenant
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}

