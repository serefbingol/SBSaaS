using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SBSaaS.Application.Features
{
    /// <summary>
    /// Kiracıların özellik (feature) limitlerini ve erişim durumlarını yöneten servis.
    /// Bu servis, bir kiracının mevcut abonelik planını ve o kiracıya özel olarak
    /// tanımlanmış geçersiz kılmaları (override) dikkate alarak çalışır.
    /// Sonuçlar, performansı artırmak için önbelleğe alınır.
    /// </summary>
    public interface IFeatureService
    {
        /// <summary>
        /// Mevcut kiracı için belirli bir özelliğin geçerli limitini asenkron olarak alır.
        /// Limit, önce kiracıya özel bir geçersiz kılma (override) olup olmadığına bakar.
        /// Yoksa, kiracının aktif abonelik planındaki limite bakar.
        /// O da yoksa, null döner.
        /// </summary>
        /// <param name="featureKey">Limitinin öğrenilmek istendiği özelliğin anahtarı (örn: 'api_calls', 'storage_gb').</param>
        /// <returns>Özellik için tanımlanmış limit değeri veya limit yoksa null.</returns>
        Task<long?> GetCurrentTenantFeatureLimitAsync(string featureKey);

        /// <summary>
        /// Belirtilen bir kiracı için tüm özelliklerin geçerli limitlerini asenkron olarak alır.
        /// Bu metot, plan limitleri ile kiracıya özel geçersiz kılmaları (override) birleştirerek nihai sonucu döner.
        /// </summary>
        /// <param name="tenantId">Limitleri öğrenilmek istenen kiracının ID'si.</param>
        /// <returns>Özellik anahtarları ve limit değerlerini içeren bir sözlük.</returns>
        Task<Dictionary<string, long>> GetAllFeaturesForTenantAsync(Guid tenantId);
    }
}
