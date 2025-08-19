using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Domain.Entities;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Common;
using SBSaaS.Domain.Entities.Billing;
using SBSaaS.Domain.Entities.Projects;
using SBSaaS.Infrastructure.Audit;
using SBSaaS.Infrastructure.Identity;

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
            // Bu index, belirli bir kiracı içindeki kullanıcıları e-posta ile aramayı destekler.
            // İş mantığı izin veriyorsa, aynı e-postanın farklı kiracılar için var olmasına izin vermek amacıyla
            // bu index benzersiz (unique) değildir. Kiracı başına benzersiz e-posta için,
            // {TenantId, NormalizedEmail} üzerinde birleşik bir benzersiz index (composite unique index) oluşturulmalıdır.
            e.HasIndex(u => new { u.TenantId, u.Email });
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
        
        // Global query filter şablonu – ITenantScoped olan tüm entity’lere uygula
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(SbsDbContext)
                    .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                method.MakeGenericMethod(entityType.ClrType)
                      .Invoke(null, new object[] { this, b });
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        ApplyEntityRules();
        return base.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Kayıt öncesi varlıklar üzerinde denetim (auditing) ve çok kiracılı (multi-tenancy) kurallarını uygular.
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
        // Eğer TenantId manuel olarak (örn: seeder tarafından) atanmamışsa, bağlamdaki tenant'ı ata.
        if (scopedEntity.TenantId == Guid.Empty)
        {
            scopedEntity.TenantId = currentTenantId;
        }

        // Atama denemesinden sonra hala TenantId boş ise bu bir hatadır.
        if (scopedEntity.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException($"Cannot save entity of type {entry.Entity.GetType().Name} without a TenantId. A valid tenant context is required.");
        }
    }

    private static void HandleModifiedTenantScopedEntity(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, Guid currentTenantId)
    {
        // Orijinal TenantId'yi veritabanından geldiği haliyle kontrol et.
        var originalTenantId = entry.OriginalValues.GetValue<Guid>(nameof(ITenantScoped.TenantId));
        if (!Equals(originalTenantId, currentTenantId))
        {
            throw new InvalidOperationException($"Cross-tenant update attempt detected. Entity belongs to tenant {originalTenantId}.");
        }

        // TenantId alanının değiştirilmesini engelle. Bu, bir verinin başka bir tenanta taşınmasını önler.
        if (entry.Property(nameof(ITenantScoped.TenantId)).IsModified)
        {
            throw new InvalidOperationException("Changing the TenantId of an existing entity is not allowed.");
        }
    }

    private static void HandleDeletedTenantScopedEntity(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, Guid currentTenantId)
    {
        // Silme işleminde de verinin orijinal sahibinin mevcut tenant olduğunu doğrula.
        var tenantIdForDelete = entry.OriginalValues.GetValue<Guid>(nameof(ITenantScoped.TenantId));
        if (!Equals(tenantIdForDelete, currentTenantId))
        {
            throw new InvalidOperationException($"Cross-tenant delete attempt detected. Entity belongs to tenant {tenantIdForDelete}.");
        }
    }

    private static void SetTenantFilter<TEntity>(SbsDbContext ctx, ModelBuilder b) where TEntity : class
    {
        b.Entity<TEntity>().HasQueryFilter(e => !typeof(ITenantScoped).IsAssignableFrom(typeof(TEntity)) ||
            ((ITenantScoped)e!).TenantId == ctx._tenant.TenantId);
    }
}