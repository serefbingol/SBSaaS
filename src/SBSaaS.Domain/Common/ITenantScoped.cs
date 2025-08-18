using System;

namespace SBSaaS.Domain.Common;

public interface ITenantScoped
{
    Guid TenantId { get; set; }
}