using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using SBSaaS.Infrastructure.Persistence;

namespace SBSaaS.Worker.Jobs
{
    /// <summary>
    /// Bu görev, faturalama dönemi sona ermiş 'usage_period' kayıtlarını işler.
    /// Varsa, plan limitlerini aşan kullanımlar için 'billing.overages' tablosuna kayıt oluşturur.
    /// Son olarak, işlenen dönemsel kullanım kayıtlarını 'kapalı' (closed) olarak işaretler.
    /// Genellikle günde bir kez, diğer agregasyon görevlerinden sonra çalıştırılması hedeflenir.
    /// </summary>
    [DisallowConcurrentExecution]
    public class PeriodClosingJob : IJob
    {
        private readonly ILogger<PeriodClosingJob> _logger;
        private readonly IDbContextFactory<SbsDbContext> _dbContextFactory;

        public PeriodClosingJob(ILogger<PeriodClosingJob> logger, IDbContextFactory<SbsDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Dönem kapatma ve aşım hesaplama görevi (PeriodClosingJob) başladı. Zaman: {time}", DateTimeOffset.UtcNow);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(context.CancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(context.CancellationToken);

            try
            {
                // Adım 1: Dönemi bitmiş ve limiti aşmış kullanımlar için 'billing.overages' tablosuna kayıt ekle.
                // Bu sorgu, dönemi biten kullanımları, aktif abonelikleri ve plan özelliklerini birleştirerek
                // limit aşımını hesaplar ve ilgili overage kaydını oluşturur.
                // ON CONFLICT, bu işlemin idempotent olmasını sağlar; yani aynı dönem için tekrar tekrar çalışsa bile
                // mükerrer overage kaydı oluşturmaz.
                var createOveragesSql = @"
WITH periods_to_process AS (
    SELECT id, tenant_id, ""key"", ""value"", period_start, period_end
    FROM metering.usage_period
    WHERE closed = false AND period_end < CURRENT_DATE
),
overage_details AS (
    SELECT
        p.tenant_id,
        s.id as subscription_id,
        p.""key"" as feature_key,
        p.period_start,
        p.period_end,
        p.""value"" as quantity_used,
        pf.limit_value,
        pf.overage_price,
        GREATEST(0, p.""value"" - pf.limit_value) as quantity_over
    FROM periods_to_process p
    JOIN billing.subscriptions s ON s.tenant_id = p.tenant_id AND s.status = 'active'
    JOIN billing.plans pl ON pl.id = s.plan_id
    JOIN billing.plan_features pf ON pf.plan_id = pl.id AND pf.feature_key = p.""key""
    WHERE pf.overage_price IS NOT NULL AND pf.overage_price > 0 AND p.""value"" > pf.limit_value
)
INSERT INTO billing.overages (id, tenant_id, subscription_id, feature_key, period_start, period_end, quantity_used, quantity_over, amount, created_at, created_by)
SELECT
    gen_random_uuid(), od.tenant_id, od.subscription_id, od.feature_key, od.period_start, od.period_end, od.quantity_used, od.quantity_over,
    od.quantity_over * od.overage_price, NOW() AT TIME ZONE 'UTC', '00000000-0000-0000-0000-000000000001'
FROM overage_details od
WHERE od.quantity_over > 0
ON CONFLICT (tenant_id, feature_key, period_start, period_end) DO NOTHING;";
                var overagesCreated = await dbContext.Database.ExecuteSqlRawAsync(createOveragesSql, context.CancellationToken);
                _logger.LogInformation("{count} adet yeni aşım (overage) kaydı oluşturuldu.", overagesCreated);

                // Adım 2: İşlenen tüm dönemleri 'kapalı' olarak işaretle.
                var closePeriodsSql = @"UPDATE metering.usage_period SET closed = true, updated_at = NOW() AT TIME ZONE 'UTC' WHERE closed = false AND period_end < CURRENT_DATE;";
                var periodsClosed = await dbContext.Database.ExecuteSqlRawAsync(closePeriodsSql, context.CancellationToken);
                _logger.LogInformation("{count} adet kullanım dönemi kapatıldı.", periodsClosed);

                await transaction.CommitAsync(context.CancellationToken);
                _logger.LogInformation("Dönem kapatma görevi başarıyla tamamlandı. Zaman: {time}", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dönem kapatma görevi sırasında bir hata oluştu. Değişiklikler geri alınıyor.");
                await transaction.RollbackAsync(context.CancellationToken);
                throw; // Quartz'ın hatayı loglaması ve yeniden deneme politikalarını uygulaması için hatayı tekrar fırlat.
            }
        }
    }
}