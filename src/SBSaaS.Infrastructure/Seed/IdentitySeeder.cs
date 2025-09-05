using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Entities.Auth;

namespace SBSaaS.Infrastructure.Seed;

/// <summary>
/// Identity verilerini (roller ve başlangıç kullanıcısı) oluşturan yardımcı sınıf.
/// </summary>
public static class IdentitySeeder
{
    /// <summary>
    /// Gerekli rollerin ve varsayılan yönetici kullanıcısının veritabanında oluşturulmasını sağlar.
    /// </summary>
    public static async Task SeedAsync(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ILogger logger)
    {
        await SeedRolesAsync(roleManager, logger);
        await SeedAdminUserAsync(userManager, logger);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager, ILogger logger)
    {
        // Mimaride tanımlanan roller (130_... dokümanı)
        string[] roleNames = { "Owner", "Admin", "Manager", "User", "Viewer" };

        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
                logger.LogInformation("'{RoleName}' rolü başarıyla oluşturuldu.", roleName);
            }
        }
    }

    private static async Task SeedAdminUserAsync(UserManager<ApplicationUser> userManager, ILogger logger)
    {
        const string adminEmail = "admin@sbsaas.local";
        var systemTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // Sistem Tenant ID'si

        if (await userManager.FindByEmailAsync(adminEmail) == null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                TenantId = systemTenantId,
                FirstName = "System",
                LastName = "Owner",
                EmailConfirmed = true // Geliştirme ortamı için e-posta doğrulaması atlanıyor.
            };

            // DİKKAT: Bu şifre sadece geliştirme ortamı içindir.
            // Üretim ortamında bu değeri güvenli bir yapılandırmadan (örn: Azure Key Vault) okuyun.
            var result = await userManager.CreateAsync(adminUser, "P@ssword123!");

            if (result.Succeeded)
            {
                logger.LogInformation("'{AdminEmail}' e-postasına sahip yönetici kullanıcı oluşturuldu.", adminEmail);
                await userManager.AddToRoleAsync(adminUser, "Owner");
                logger.LogInformation("Yönetici kullanıcıya 'Owner' rolü atandı.");
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    logger.LogError("Yönetici kullanıcı oluşturulurken hata: {ErrorDescription}", error.Description);
                }
            }
        }
    }
}
