using Microsoft.AspNetCore.Identity;
using SBSaaS.Domain.Common;

namespace SBSaaS.Domain.Entities.Auth;

/// <summary>
/// Uygulamadaki bir kullanıcıyı temsil eder ve temel IdentityUser sınıfını genişletir.
/// </summary>
public class ApplicationUser : IdentityUser, ITenantScoped
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>
    /// Bu kullanıcının ait olduğu kiracının kimliği.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Kullanıcının token yenileme anahtarı.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Token yenileme anahtarının geçerlilik süresi.
    /// </summary>
    public DateTime RefreshTokenExpiryTime { get; set; }
}