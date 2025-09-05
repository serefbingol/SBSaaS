namespace SBSaaS.API.Middleware;

/// <summary>
/// Bir endpoint'in veya controller'ın TenantMiddleware tarafından yapılan
/// 'X-Tenant-Id' başlık kontrolünden muaf tutulmasını sağlar.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AllowAnonymousTenantAttribute : Attribute
{
}