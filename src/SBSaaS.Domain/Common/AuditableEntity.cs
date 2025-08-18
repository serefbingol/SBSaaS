using System;

namespace SBSaaS.Domain.Common;

public abstract class AuditableEntity
{
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
}

