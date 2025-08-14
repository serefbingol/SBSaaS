namespace SBSaaS.Domain.Entities.Billing;

public class SubscriptionPlan
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;   // PRO, TEAM, ENTERPRISE
    public string Name { get; set; } = default!;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "TRY";
    public bool IsActive { get; set; } = true;
}