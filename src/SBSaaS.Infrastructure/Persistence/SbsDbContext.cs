using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Common;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Entities.Auth;
using SBSaaS.Domain.Entities.Billing;
using SBSaaS.Domain.Entities.Metering;
using SBSaaS.Domain.Entities.Invitations;
using SBSaaS.Domain.Entities.Projects;
using SBSaaS.Infrastructure.Audit;
using SBSaaS.Infrastructure.Localization;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace SBSaaS.Infrastructure.Persistence;

public class SbsDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
{
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;

    public SbsDbContext(
        DbContextOptions<SbsDbContext> options,
        ITenantContext tenant,
        ICurrentUser currentUser) : base(options)
    {
        _tenant = tenant;
        _currentUser = currentUser;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ChangeLog> ChangeLogs => Set<ChangeLog>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Translation> Translations => Set<Translation>();
    public DbSet<SBSaaS.Domain.Entities.File> Files => Set<SBSaaS.Domain.Entities.File>();

    // Faz 4 - Metering
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<UsageDaily> UsageDailies => Set<UsageDaily>();
    public DbSet<UsagePeriod> UsagePeriods => Set<UsagePeriod>();
    public DbSet<ExternalSyncState> ExternalSyncStates => Set<ExternalSyncState>();

    // This property is used by the expression in the global query filter.
    private Guid CurrentTenantId => _tenant.TenantId;

    // Bu satırı diğer DbSet'lerinizin yanına ekleyin:
    // ASP.NET Core Data Protection anahtarlarını saklamak için kullanılır.
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = default!;
    public DbSet<FeatureOverride> FeatureOverrides => Set<FeatureOverride>();
    public DbSet<QuotaUsage> QuotaUsages => Set<QuotaUsage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Tenant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.TenantId).IsRequired();

            // In a multi-tenant structure, an email should be usable in multiple tenants.
            // However, the email must be unique within the same tenant.
            // ASP.NET Core Identity uses the `NormalizedEmail` field for email comparisons.
            // Therefore, we create a composite unique index on `TenantId` and `NormalizedEmail` to ensure uniqueness.
            e.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).IsUnique();
        });

        b.Entity<ChangeLog>(e => // A2 - Audit Logging
        {
            e.ToTable("change_log", schema: "audit");
            e.HasKey(x => x.Id);

            e.Property(x => x.TableName).HasMaxLength(128);
            e.Property(x => x.Operation).HasMaxLength(16);
            e.HasIndex(x => new { x.TenantId, x.UtcDate });
            e.HasIndex(x => new { x.TableName, x.UtcDate });
            e.HasIndex(x => x.Operation);
        });

        // Faz 4 - Metering
        b.Entity<UsageEvent>(e =>
        {
            e.ToTable("usage_event", schema: "metering");
            e.HasKey(x => x.Id);
            e.Property(x => x.Quantity).HasColumnType("numeric(18,6)");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'").ValueGeneratedOnAdd();

            // Idempotency key to prevent duplicate events
            e.HasIndex(x => new { x.TenantId, x.Key, x.IdempotencyKey }).IsUnique();

            // Index for querying by time
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
        });

        b.Entity<UsageDaily>(e =>
        {
            e.ToTable("usage_daily", schema: "metering");
            e.HasKey(x => new { x.TenantId, x.Key, x.Day });
            e.Property(x => x.Quantity).HasColumnType("numeric(18,6)");
        });

        b.Entity<UsagePeriod>(e =>
        {
            e.ToTable("usage_period", schema: "metering");
            e.HasKey(x => new { x.TenantId, x.Key, x.PeriodStart, x.PeriodEnd });
            e.Property(x => x.Quantity).HasColumnType("numeric(18,6)");
        });

        b.Entity<ExternalSyncState>(e =>
        {
            e.ToTable("external_sync_state", schema: "metering");
            // SQL şemasındaki UNIQUE kısıtlaması, başka bir aday anahtar olmadığı için
            // en iyi şekilde birincil anahtar olarak temsil edilir.
            e.HasKey(x => new { x.Provider, x.TenantId, x.Key });
        });

        b.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        b.Entity<Invitation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.Token).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique(false);
            e.HasIndex(x => x.Token).IsUnique();
        });

        b.Entity<Translation>(b =>
        {
            b.ToTable("i18n_translations");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.Key, x.Culture }).IsUnique();
        });

        b.Entity<SBSaaS.Domain.Entities.File>(e =>
        {
            e.ToTable("Files"); // Explicitly set table name in public schema
            e.HasKey(x => x.Id);
            e.Property(x => x.StorageObjectName).IsRequired();
            e.Property(x => x.BucketName).IsRequired();
            e.Property(x => x.Checksum).IsRequired();
            e.Property(x => x.ContentType).IsRequired();
            e.Property(x => x.OriginalFileName).IsRequired();
            e.Property(x => x.UploadedByUserId).IsRequired();

            // Configure the JSONB column for PostgreSQL
            e.Property(x => x.Metadata).HasColumnType("jsonb");
        });
        // Faz 5 - Billing
        b.Entity<FeatureOverride>(e =>
        {
            // Dokümanda belirtildiği gibi 'billing' şemasına haritala
            e.ToTable("feature_override", "billing");

            e.HasKey(x => x.Id);

            // Bir kiracının belirli bir özellik için sadece bir tane geçersiz kılma (override) kaydı olabilir.
            e.HasIndex(x => new { x.TenantId, x.FeatureKey }).IsUnique();

            e.Property(x => x.FeatureKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
        });

        b.Entity<QuotaUsage>(e =>
        {
            // Dokümanda belirtildiği gibi 'billing' şemasına haritala
            e.ToTable("quota_usage", "billing");

            e.HasKey(x => x.Id);

            // Bir kiracının, belirli bir özellik için, belirli bir dönemde tek bir kullanım kaydı olabilir.
            // Bu indeks, kota kontrolü ve güncelleme performansı için kritiktir.
            e.HasIndex(x => new { x.TenantId, x.FeatureKey, x.PeriodStart }).IsUnique();

            e.Property(x => x.FeatureKey).HasMaxLength(100).IsRequired();
        });

        b.Entity<SubscriptionPlan>(e =>
        {
            e.ToTable("plans", "billing");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.BillingCycle).HasMaxLength(50).IsRequired();
            e.Property(x => x.Price).HasColumnType("decimal(18, 2)");

            // Bir planın birden çok özelliği olabilir. Plan silinirse özellikleri de silinir.
            e.HasMany(p => p.Features)
             .WithOne(f => f.Plan)
             .HasForeignKey(f => f.PlanId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Subscription>(e =>
        {
            e.ToTable("subscriptions", "billing");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.Property(x => x.Status).HasMaxLength(50).IsRequired();

            // Bir abonelik bir plana aittir. Plan silinirse ve abonelik varsa silme işlemi engellenir.
            e.HasOne(s => s.Plan)
             .WithMany() // Bir planın birden çok aboneliği olabilir.
             .HasForeignKey(s => s.PlanId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PlanFeature>(e =>
        {
            e.ToTable("plan_features", "billing");
            e.HasKey(x => x.Id);

            // Bir plan içinde her özellik anahtarı benzersiz olmalıdır.
            e.HasIndex(x => new { x.PlanId, x.FeatureKey }).IsUnique();
            e.Property(x => x.FeatureKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.OveragePrice).HasColumnType("decimal(18, 4)");
        });

        // ... metodun geri kalanı ...

        // Global query filter to apply tenancy and soft-delete rules.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            Expression? filterExpression = null;

            // 1. Apply Tenant Filter if applicable
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var property = Expression.Property(parameter, nameof(ITenantScoped.TenantId));
                var currentTenantIdProperty = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
                filterExpression = Expression.Equal(property, currentTenantIdProperty);
            }

            // 2. Apply Soft-Delete Filter if applicable
            if (typeof(BaseAuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                var isDeletedProperty = Expression.Property(parameter, nameof(BaseAuditableEntity.IsDeleted));
                var falseConstant = Expression.Constant(false);
                var softDeleteFilter = Expression.Equal(isDeletedProperty, falseConstant);

                filterExpression = filterExpression == null
                    ? softDeleteFilter
                    : Expression.AndAlso(filterExpression, softDeleteFilter);
            }

            if (filterExpression != null)
            {
                b.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(filterExpression, parameter));
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        ApplyEntityRules();
        return base.SaveChangesAsync(ct);
    }

    public override int SaveChanges()
    {
        ApplyEntityRules();
        return base.SaveChanges();
    }

    /// <summary>
    /// Applies auditing and multi-tenancy rules before saving entities.
    /// </summary>
    private void ApplyEntityRules()
    {
        var now = DateTimeOffset.UtcNow;
        var currentTenantId = _tenant.TenantId;
        var currentUserId = _currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries())
        {
            // Apply rules for entities that have audit fields (Created, Modified, Deleted)
            if (entry.Entity is BaseAuditableEntity auditableEntity)
            {
                ApplyAuditableEntityRules(entry, auditableEntity, now, currentUserId);
            }

            // Apply rules for entities that are scoped to a tenant
            if (entry.Entity is ITenantScoped scopedEntity)
            {
                ApplyTenantScopedEntityRules(entry, scopedEntity, currentTenantId);
            }
        }
    }

    private static void ApplyAuditableEntityRules(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, BaseAuditableEntity auditableEntity, DateTimeOffset now, Guid? userId)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                auditableEntity.CreatedBy = userId;
                auditableEntity.CreatedAt = now;
                break;

            case EntityState.Modified:
                auditableEntity.LastModifiedBy = userId;
                auditableEntity.LastModifiedAt = now;
                break;

            case EntityState.Deleted:
                // Intercept the deletion and convert it to a soft-delete
                entry.State = EntityState.Modified;
                auditableEntity.IsDeleted = true;
                auditableEntity.DeletedBy = userId;
                auditableEntity.DeletedAt = now;
                break;
        }
    }

    private static void ApplyTenantScopedEntityRules(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, ITenantScoped scopedEntity, Guid currentTenantId)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                HandleAddedTenantScopedEntity(entry, scopedEntity, currentTenantId);
                break;

            case EntityState.Modified:
                HandleModifiedTenantScopedEntity(entry, currentTenantId);
                break;

            case EntityState.Deleted:
                HandleDeletedTenantScopedEntity(entry, currentTenantId);
                break;
        }
    }

    private static void HandleAddedTenantScopedEntity(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, ITenantScoped scopedEntity, Guid currentTenantId)
    {
        // If TenantId has not been assigned manually (e.g., by a seeder), assign the tenant from the context.
        if (scopedEntity.TenantId == Guid.Empty)
        {
            scopedEntity.TenantId = currentTenantId;
        }

        // If TenantId is still empty after the assignment attempt, it's an error.
        if (scopedEntity.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException($"Cannot save entity of type {entry.Entity.GetType().Name} without a TenantId. A valid tenant context is required.");
        }
    }

    private static void HandleModifiedTenantScopedEntity(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, Guid currentTenantId)
    {
        // Check the original TenantId as it came from the database.
        var originalTenantId = entry.OriginalValues.GetValue<Guid>(nameof(ITenantScoped.TenantId));
        if (!Equals(originalTenantId, currentTenantId))
        {
            throw new InvalidOperationException($"Cross-tenant update attempt detected. Entity belongs to tenant {originalTenantId}.");
        }

        // Prevent changing the TenantId field. This prevents moving data to another tenant.
        if (entry.Property(nameof(ITenantScoped.TenantId)).IsModified)
        {
            throw new InvalidOperationException("Changing the TenantId of an existing entity is not allowed.");
        }
    }

    private static void HandleDeletedTenantScopedEntity(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, Guid currentTenantId)
    {
        // Also verify that the original owner of the data is the current tenant during deletion.
        var tenantIdForDelete = entry.OriginalValues.GetValue<Guid>(nameof(ITenantScoped.TenantId));
        if (!Equals(tenantIdForDelete, currentTenantId))
        {
            throw new InvalidOperationException($"Cross-tenant delete attempt detected. Entity belongs to tenant {tenantIdForDelete}.");
        }
    }
}
