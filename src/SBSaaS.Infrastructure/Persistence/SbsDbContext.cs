using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Domain.Entities;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Common;
using SBSaaS.Domain.Entities.Billing;
using SBSaaS.Domain.Entities.Projects;
using SBSaaS.Infrastructure.Audit;

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

        b.Entity<ChangeLog>(e =>
        {
            e.ToTable("change_log", schema: "audit");
            e.HasKey(x => x.Id);
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

    // Yeni eklenen/updated entity’lerde TenantId ve zaman damgalarını otomatik ata
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var currentTenantId = _tenant.TenantId;

        // TenantId'si olmayan bir context ile tenant-scoped veri yazmaya çalışmayı engelle.
        // Bu, sistem düzeyinde (tenant-agnostic) işlemlerin yanlışlıkla tenant verisine dokunmasını önler.
        if (currentTenantId == Guid.Empty && ChangeTracker.Entries<ITenantScoped>().Any(e => e.State != EntityState.Unchanged))
        {
            throw new InvalidOperationException("A valid tenant context is required to save tenant-scoped entities.");
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditableEntity aud)
            {
                if (entry.State == EntityState.Added) aud.CreatedUtc = now;
                if (entry.State == EntityState.Modified) aud.UpdatedUtc = now;
            }

            if (entry.Entity is ITenantScoped scoped)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        // Yeni kayıtlara mevcut tenantId'yi otomatik ata.
                        scoped.TenantId = currentTenantId;
                        break;

                    case EntityState.Modified:
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
                        break;
                }
            }
        }
        return base.SaveChangesAsync(ct);
    }

    private static void SetTenantFilter<TEntity>(SbsDbContext ctx, ModelBuilder b) where TEntity : class
    {
        b.Entity<TEntity>().HasQueryFilter(e => !typeof(ITenantScoped).IsAssignableFrom(typeof(TEntity)) ||
            ((ITenantScoped)e!).TenantId == ctx._tenant.TenantId);
    }
}