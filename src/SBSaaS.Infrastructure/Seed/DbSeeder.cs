using Microsoft.EntityFrameworkCore;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Entities.Projects;
using SBSaaS.Infrastructure.Persistence;

namespace SBSaaS.Infrastructure.Seed;

public static class DbSeeder
{
    public static async Task SeedAsync(SbsDbContext db)
    {
        var systemTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        await db.Database.MigrateAsync();

        if (!await db.Tenants.AnyAsync(t => t.Id == systemTenantId))
        {
            var systemTenant = new Tenant
            {
                Id = systemTenantId,
                Name = "System Tenant",
                Culture = "tr-TR",
                TimeZone = "Europe/Istanbul"
            };
            db.Tenants.Add(systemTenant);
            await db.SaveChangesAsync();
        }

        // Örnek proje kaydı (seed), aktif tenant bağlamı gerektiği için manuel set ediliyor
        if (!await db.Projects.AnyAsync(p => p.TenantId == systemTenantId))
        {
            db.Projects.Add(new Project
            {
                Id = Guid.NewGuid(),
                TenantId = systemTenantId,
                Code = "PRJ-001",
                Name = "Kickoff",
                Description = "Initial seed project"
            });
            await db.SaveChangesAsync();
        }
    }
}
