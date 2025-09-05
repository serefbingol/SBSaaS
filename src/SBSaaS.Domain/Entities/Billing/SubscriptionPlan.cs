using System.Collections.Generic;
using SBSaaS.Domain.Entities.Billing;

namespace SBSaaS.Domain.Entities.Billing;

public class SubscriptionPlan
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;   // PRO, TEAM, ENTERPRISE
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "TRY";
    public string BillingCycle { get; set; } = default!; // e.g., "monthly", "yearly"
    public bool IsActive { get; set; } = true;

    public ICollection<PlanFeature> Features { get; set; } = new List<PlanFeature>();
}