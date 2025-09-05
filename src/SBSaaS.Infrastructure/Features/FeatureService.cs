using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SBSaaS.Application.Features;
using SBSaaS.Application.Interfaces;
using SBSaaS.Infrastructure.Persistence;

namespace SBSaaS.Infrastructure.Features
{
    /// <summary>
    /// IFeatureService'in cache destekli implementasyonu.
    /// Kiracıların özellik limitlerini veritabanından okur ve performansı artırmak için
    /// belirli bir süre bellekte tutar.
    /// </summary>
    public class FeatureService : IFeatureService
    {
        private readonly SbsDbContext _dbContext;
        private readonly ITenantContext _tenantContext;
        private readonly IMemoryCache _cache;

        // Cache'de tutulacak verinin ne kadar süre geçerli olacağı.
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public FeatureService(SbsDbContext dbContext, ITenantContext tenantContext, IMemoryCache cache)
        {
            _dbContext = dbContext;
            _tenantContext = tenantContext;
            _cache = cache;
        }

        /// <inheritdoc />
        public async Task<long?> GetCurrentTenantFeatureLimitAsync(string featureKey)
        {
            var tenantId = _tenantContext.TenantId;
            if (tenantId == Guid.Empty)
            {
                // Geçerli bir kiracı bağlamı yoksa limit de yoktur.
                return null;
            }

            // Kiracının tüm özellik limitlerini içeren sözlüğü cache'den al veya oluştur.
            var tenantFeatures = await GetOrSetTenantFeaturesCache(tenantId);

            return tenantFeatures.TryGetValue(featureKey, out var limit) ? limit : null;
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, long>> GetAllFeaturesForTenantAsync(Guid tenantId)
        {
            // Kiracının tüm özellik limitlerini içeren sözlüğü cache'den al veya oluştur ve doğrudan döndür.
            var tenantFeatures = await GetOrSetTenantFeaturesCache(tenantId);
            return tenantFeatures;
        }

        /// <summary>
        /// Belirtilen kiracının tüm özellik limitlerini cache'den alır.
        /// Cache'de yoksa veritabanından okur, cache'e yazar ve döndürür.
        /// </summary>
        private async Task<Dictionary<string, long>> GetOrSetTenantFeaturesCache(Guid tenantId)
        {
            var cacheKey = $"features_{tenantId}"; // Bu anahtar, AdminFeaturesController'daki ile tutarlı olmalıdır.

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;

                // 1. Kiracıya özel atanmış ve süresi dolmamış geçersiz kılmaları (override) al.
                var overrides = await _dbContext.FeatureOverrides
                    .AsNoTracking()
                    .Where(o => o.TenantId == tenantId && (o.ExpiresAt == null || o.ExpiresAt > DateTime.UtcNow))
                    .ToDictionaryAsync(o => o.FeatureKey, o => o.Limit);

                // 2. Kiracının aktif aboneliğindeki planın özelliklerini al.
                // En güncel aktif aboneliği bularak plan özelliklerini alıyoruz.
                // Bu, yanlışlıkla birden fazla aktif abonelik olması durumunda tutarlı sonuç sağlar.
                var planFeatures = new Dictionary<string, long>();
                var activeSubscription = await _dbContext.Subscriptions
                    .AsNoTracking()
                    .Include(s => s.Plan)
                    .ThenInclude(p => p.Features)
                    .Where(s => s.TenantId == tenantId && s.Status == "active")
                    .OrderByDescending(s => s.BillingPeriodStart)
                    .FirstOrDefaultAsync();

                if (activeSubscription?.Plan?.Features != null)
                {
                    planFeatures = activeSubscription.Plan.Features
                        .ToDictionary(pf => pf.FeatureKey, pf => pf.LimitValue);
                }

                // 3. Plan özelliklerini ve geçersiz kılmaları birleştir.
                // Geçersiz kılmalar (overrides) her zaman önceliklidir.
                // Önce plan özellikleri eklenir, sonra override'lar üzerine yazar.
                foreach (var (key, value) in overrides)
                {
                    planFeatures[key] = value; // Varsa üzerine yazar, yoksa ekler.
                }

                return planFeatures;
            }) ?? new Dictionary<string, long>();
        }
    }
}