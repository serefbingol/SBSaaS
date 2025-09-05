using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using SBSaaS.Infrastructure.Persistence;

namespace SBSaaS.Worker.Jobs
{
    /// <summary>
    /// Bu görev, 'usage_daily' tablosundaki günlük özetleri, her kiracının
    /// faturalama dönemine göre 'usage_period' tablosunda toplar.
    /// Genellikle günde bir kez, DailyAggregationJob'dan sonra çalıştırılması hedeflenir.
    /// </summary>
    [DisallowConcurrentExecution]
    public class PeriodAggregationJob : IJob
    {
        private readonly ILogger<PeriodAggregationJob> _logger;
        private readonly IDbContextFactory<SbsDbContext> _dbContextFactory;

        public PeriodAggregationJob(ILogger<PeriodAggregationJob> logger, IDbContextFactory<SbsDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Dönemsel kullanım verisi toplama görevi (PeriodAggregationJob) başladı. Zaman: {time}", DateTimeOffset.UtcNow);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(context.CancellationToken);

            // Bu sorgu, her kiracının aktif faturalama dönemi içindeki günlük kullanım verilerini
            // alıp, dönemsel kullanım tablosuna yazar veya günceller.
            var sql = @"
INSERT INTO metering.usage_period (id, tenant_id, ""key"", period_start, period_end, ""value"", closed, created_at, created_by)
SELECT
    gen_random_uuid(),
    ud.tenant_id,
    ud.""key"",
    s.billing_period_start,
    s.billing_period_end,
    SUM(ud.""value""),
    false, -- Yeni veya güncellenen dönemler her zaman 'açık' durumdadır.
    NOW() AT TIME ZONE 'UTC',
    '00000000-0000-0000-0000-000000000001' -- Sistem Kullanıcısı
FROM metering.usage_daily ud
JOIN billing.subscriptions s ON ud.tenant_id = s.tenant_id
WHERE
    s.status = 'active' AND ud.""day"" >= s.billing_period_start AND ud.""day"" < s.billing_period_end
GROUP BY ud.tenant_id, ud.""key"", s.billing_period_start, s.billing_period_end
ON CONFLICT (tenant_id, ""key"", period_start, period_end) DO UPDATE
SET ""value"" = usage_period.""value"" + EXCLUDED.""value"", updated_at = NOW() AT TIME ZONE 'UTC';";

            var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, context.CancellationToken);
            _logger.LogInformation("Dönemsel kullanım verisi toplama görevi tamamlandı. {rows} satır etkilendi/güncellendi. Zaman: {time}", rowsAffected, DateTimeOffset.UtcNow);
        }
    }
}