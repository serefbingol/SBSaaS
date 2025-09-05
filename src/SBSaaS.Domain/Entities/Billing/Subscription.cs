namespace SBSaaS.Domain.Entities.Billing;

/// <summary>
/// Bir kiracının bir abonelik planına olan üyeliğini temsil eder.
/// </summary>
public class Subscription
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;

    /// <summary>
    /// Aboneliğin durumu (örn: 'active', 'trialing', 'past_due', 'canceled').
    /// </summary>
    public string Status { get; set; } = default!;

    /// <summary>
    /// Mevcut faturalama döneminin başlangıç tarihi.
    /// </summary>
    public DateTime BillingPeriodStart { get; set; }

    /// <summary>
    /// Mevcut faturalama döneminin bitiş tarihi.
    /// </summary>
    public DateTime BillingPeriodEnd { get; set; }

    /// <summary>
    /// Aboneliğin iptal edildiği tarih (varsa).
    /// </summary>
    public DateTime? CanceledAt { get; set; }

    public bool AutoRenew { get; set; }
}