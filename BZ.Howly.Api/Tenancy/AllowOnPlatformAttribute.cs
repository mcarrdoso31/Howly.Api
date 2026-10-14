namespace BZ.Howly.Api.Tenancy;

/// <summary>
/// Marks controllers that also exist on the platform portal (sign in, own account, the portal itself). Everything
/// else answers 404 there: the platform has no customers nor processes (see <see cref="TenantResolutionMiddleware"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowOnPlatformAttribute : Attribute;
