using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using SBSaaS.Infrastructure.Persistence;

namespace SBSaaS.Worker.Jobs
{
    /// <summary>
    /// Bu görev, usage_event tablosundaki ham verileri günlük olarak toplar ve
    /// usage_daily tablosuna özetlenmiş olarak yazar.
    /// Genellikle her gece bir kez çalıştırılması hedeflenir.
    /// </summary>
    [DisallowConcurrentExecution] // Aynı job'ın birden fazla instance'ının aynı anda çalışmasını engeller.
    public class DailyAggregationJob : IJob
    {
        private readonly ILogger<DailyAggregationJob> _logger;
        private readonly IDbContextFactory<SbsDbContext> _dbContextFactory;

        public DailyAggregationJob(ILogger<DailyAggregationJob> logger, IDbContextFactory<SbsDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Günlük kullanım verisi toplama görevi (DailyAggregationJob) başladı. Zaman: {time}", DateTimeOffset.UtcNow);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(context.CancellationToken);

            // Genellikle bir önceki günün verisi toplanır.
            var periodStart = DateTime.UtcNow.Date.AddDays(-1);
            var periodEnd = periodStart.AddDays(1);

            // Ham SQL kullanarak verimli bir şekilde toplama ve ekleme/güncelleme (UPSERT) yapalım.
            // Bu, binlerce usage_event'i belleğe almaktan çok daha performanslıdır.
            // ON CONFLICT, PostgreSQL'e özgü bir ifadedir ve mükerrer kayıtları önleyip mevcut kaydı güncellememizi sağlar.
            var sql = @"
INSERT INTO metering.usage_daily (id, tenant_id, ""key"", ""day"", ""value"", created_at, created_by)
SELECT
    gen_random_uuid(),
    tenant_id,
    ""key"",
    DATE_TRUNC('day', ""timestamp"" AT TIME ZONE 'UTC'),
    SUM(""value""),
    NOW() AT TIME ZONE 'UTC',
    '00000000-0000-0000-0000-000000000001' -- Sistem Kullanıcısı ID'si
FROM metering.usage_event
WHERE
    ""timestamp"" >= @p0 AND ""timestamp"" < @p1
GROUP BY
    tenant_id, ""key"", DATE_TRUNC('day', ""timestamp"" AT TIME ZONE 'UTC')
ON CONFLICT (tenant_id, ""key"", ""day"") DO UPDATE
SET
    ""value"" = usage_daily.""value"" + EXCLUDED.""value"",
    updated_at = NOW() AT TIME ZONE 'UTC';";

            var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, periodStart, periodEnd, context.CancellationToken);
            _logger.LogInformation("Günlük kullanım verisi toplama görevi tamamlandı. {rows} satır etkilendi/güncellendi. Zaman: {time}", rowsAffected, DateTimeOffset.UtcNow);
        }
    }
}
