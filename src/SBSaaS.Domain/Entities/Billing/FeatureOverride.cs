using System;
using SBSaaS.Domain.Common;

namespace SBSaaS.Domain.Entities.Billing
{
    /// <summary>
    /// Belirli bir kiracı için bir plan özelliğinin varsayılan limitini geçersiz kılar.
    /// Örneğin, bir 'pro' planın normalde 10,000 API çağrı limiti varken,
    /// bu tablo kullanılarak belirli bir kiracıya özel 15,000 limit atanabilir.
    /// </summary>
    public class FeatureOverride : BaseAuditableEntity, ITenantScoped
    {
        /// <summary>
        /// Geçersiz kılmanın uygulandığı kiracının kimliği.
        /// </summary>
        public Guid TenantId { get; set; }

        /// <summary>
        /// Geçersiz kılınan özelliğin anahtarı (örn: 'api_calls', 'storage_gb').
        /// PlanFeature'daki FeatureKey ile eşleşir.
        /// </summary>
        public string FeatureKey { get; set; } = null!;

        /// <summary>
        /// Kiracı için atanan yeni limit değeri.
        /// </summary>
        public long Limit { get; set; }

        /// <summary>
        /// Bu geçersiz kılmanın ne zaman sona ereceği (opsiyonel).
        /// Null ise, kalıcıdır.
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// Yönetici tarafından eklenen notlar.
        /// </summary>
        public string? Notes { get; set; }
    }
}