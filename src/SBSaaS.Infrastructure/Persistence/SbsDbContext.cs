using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Domain.Entities;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Entities.Billing;
using SBSaaS.Domain.Entities.Projects;

namespace SBSaaS.Infrastructure.Persistence;

public class SbsDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ITenantContext _tenantContext;

    public SbsDbContext(DbContextOptions<SbsDbContext> options, ITenantContext tenantContext) : base(options)
        => _tenantContext = tenantContext;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(b =>
        {
            b.Property(x => x.TenantId).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique(false);
        });
        builder.Entity<SBSaaS.Infrastructure.Audit.ChangeLog>(b =>
        {
            b.ToTable("change_log", schema: "audit");
            b.HasKey(x => x.Id);
        });

        builder.Entity<Project>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        // Global query filter örneği: Identity tablolarına doğrudan filtre koymak riskli olabilir,
        // business tablolarında TenantId filtresi uygulayabilirsin. Örnek entity:
        // builder.Entity<YourEntity>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
    }
}