using System;
using SBSaaS.Domain.Common;

namespace SBSaaS.Domain.Entities.Billing
{
    /// <summary>
    /// Kiracıların belirli bir dönemdeki kota kullanımlarını izler.
    /// Örneğin, bir kiracının 'api_calls' kotasından bu ay ne kadar kullandığını tutar.
    /// Bu tablo, rate limiting mekanizması tarafından sıkça güncellenir.
    /// </summary>
    public class QuotaUsage : BaseEntity, ITenantScoped
    {
        /// <summary>
        /// Kullanımın ait olduğu kiracının kimliği.
        /// </summary>
        public Guid TenantId { get; set; }

        /// <summary>
        /// Kullanımı izlenen özelliğin anahtarı (örn: 'api_calls').
        /// </summary>
        public string FeatureKey { get; set; } = null!;

        /// <summary>
        /// Bu dönem içinde yapılan kullanım miktarı.
        /// </summary>
        public long Usage { get; set; }

        /// <summary>
        /// Bu kullanım sayacının geçerli olduğu dönemin başlangıç tarihi.
        /// </summary>
        public DateTime PeriodStart { get; set; }

        /// <summary>
        /// Bu kullanım sayacının sıfırlanacağı dönemin bitiş tarihi.
        /// </summary>
        public DateTime PeriodEnd { get; set; }
    }
}