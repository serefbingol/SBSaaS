using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Entities.Metering;
using SBSaaS.Infrastructure.Persistence;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SBSaaS.Infrastructure.Services;

public class MeteringService : IMeteringService
{
    private readonly SbsDbContext _dbContext;
    private readonly ILogger<MeteringService> _logger;

    // PostgreSQL'in benzersiz kısıtlama ihlali (unique constraint violation) için hata kodu.
    private const string UniqueViolationPostgresCode = "23505";

    public MeteringService(SbsDbContext dbContext, ILogger<MeteringService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task RecordUsageAsync(Guid tenantId, string key, decimal quantity, string source, string idempotencyKey, DateTimeOffset? occurredAt = null, CancellationToken ct = default)
    {
        var usageEvent = new UsageEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = key,
            Quantity = quantity,
            Source = source,
            IdempotencyKey = idempotencyKey,
            // `OccurredAt` alanı DateTimeOffset tipindedir. `.UtcDateTime` kullanımı gereksizdir ve offset bilgisini kaybettirebilir.
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow
        };

        _dbContext.UsageEvents.Add(usageEvent);
        
        try { await _dbContext.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == UniqueViolationPostgresCode)
        {
            // Bu beklenen bir durumdur. Idempotency anahtarı, aynı olayın mükerrer işlenmesini engeller.
            // Hata olarak değil, uyarı olarak loglanır ve exception yutulur.
            _logger.LogWarning(
                "Duplicate usage event detected and ignored for Tenant {TenantId}, Key {Key}, IdempotencyKey {IdempotencyKey}. This is expected for idempotency.",
                tenantId, key, idempotencyKey);
        }
    }
}
