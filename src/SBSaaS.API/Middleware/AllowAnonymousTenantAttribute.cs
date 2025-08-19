namespace SBSaaS.API.Middleware;

/// <summary>
/// Specifies that an endpoint can be accessed without an X-Tenant-Id header.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowAnonymousTenantAttribute : Attribute { }

