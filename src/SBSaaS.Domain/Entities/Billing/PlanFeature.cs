using System;

namespace SBSaaS.Domain.Entities.Billing
{
    /// <summary>
    /// Bir abonelik planının sahip olduğu özellikleri ve bu özelliklerin limitlerini tanımlar.
    /// </summary>
    public class PlanFeature
    {
        public Guid Id { get; set; }
        public Guid PlanId { get; set; }
        public SubscriptionPlan Plan { get; set; } = null!;
        public string FeatureKey { get; set; } = null!;
        public long LimitValue { get; set; }
        public decimal? OveragePrice { get; set; }
    }
}