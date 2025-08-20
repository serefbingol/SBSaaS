using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Common;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Entities.Auth;
using SBSaaS.Domain.Entities.Billing;
using SBSaaS.Domain.Entities.Invitations;
using SBSaaS.Domain.Entities.Projects;
using SBSaaS.Infrastructure.Audit;
using SBSaaS.Infrastructure.Localization;

namespace SBSaaS.Infrastructure.Persistence;

public class SbsDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext _tenant;

    public SbsDbContext(DbContextOptions<SbsDbContext> options, ITenantContext tenant) : base(options)
        => _tenant = tenant;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ChangeLog> ChangeLogs => Set<ChangeLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Translation> Translations => Set<Translation>();

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

        // Global query filter to apply tenancy rules.
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(ITenantScoped.TenantId));
                var tenantId = Expression.Property(Expression.Field(Expression.Constant(this), "_tenant"), "TenantId");
                var body = Expression.Equal(property, tenantId);
                var lambda = Expression.Lambda(body, parameter);

                b.Entity(entityType.ClrType).HasQueryFilter(lambda);
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

        foreach (var entry in ChangeTracker.Entries())
        {
            ApplyAuditableEntityRules(entry, now);
            ApplyTenantScopedEntityRules(entry, currentTenantId);
        }
    }

    private static void ApplyAuditableEntityRules(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Entity is not AuditableEntity auditableEntity) return;

        if (entry.State == EntityState.Added)
        {
            auditableEntity.CreatedUtc = now;
        }
        else if (entry.State == EntityState.Modified)
        {
            auditableEntity.UpdatedUtc = now;
        }
    }

    private static void ApplyTenantScopedEntityRules(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, Guid currentTenantId)
    {
        if (entry.Entity is not ITenantScoped scopedEntity) return;

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
