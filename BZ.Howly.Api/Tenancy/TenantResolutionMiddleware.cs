using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BZ.Howly.Api.Tenancy;

/// <summary>
/// Finds the consultancy of each request from its host (<c>bia.howly.app</c> → "bia") and sets it on the
/// <see cref="ITenantContext"/> before anything reads data. Unknown portal: 404; suspended: 403. Stateless across
/// instances: the only state is a short per-instance cache.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, TenantCache cache, IOptions<TenancyOptions> options, IHostEnvironment environment)
{
    /// <summary>Routes that are not part of a portal (health checks for the load balancer, the dev API description).</summary>
    private static readonly PathString[] TenantlessPaths = ["/health", "/openapi"];

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ITenantRepository tenants, IProblemDetailsService problemDetails)
    {
        if (TenantlessPaths.Any(path => context.Request.Path.StartsWithSegments(path)))
        {
            await next(context);
            return;
        }

        // The bare-domain fallback is a development convenience only.
        var allowFallback = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var slug = SlugOf(context.Request.Host.Host, options.Value, allowFallback);
        var tenant = slug is null ? null : await FindAsync(slug, tenants, context.RequestAborted);
        if (tenant is null)
        {
            await RefuseAsync(context, problemDetails, StatusCodes.Status404NotFound, "This portal does not exist.", "tenant_not_found");
            return;
        }
        if (tenant.Status == TenantStatus.Suspended)
        {
            await RefuseAsync(context, problemDetails, StatusCodes.Status403Forbidden, "This portal is suspended.", "tenant_suspended");
            return;
        }

        // The platform portal has no business routes: they do not exist there (before authorization, so not even a 403).
        if (tenant.IsPlatform && context.GetEndpoint()?.Metadata.GetMetadata<AllowOnPlatformAttribute>() is null)
        {
            await RefuseAsync(context, problemDetails, StatusCodes.Status404NotFound, "Not found.", "not_on_platform");
            return;
        }

        tenantContext.Set(tenant);
        await next(context);
    }

    /// <summary>The first label under a base domain; the fallback (dev/tests) for the bare base domain; else null.</summary>
    public static string? SlugOf(string host, TenancyOptions options, bool allowFallback)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        foreach (var baseDomain in options.BaseDomains.Select(d => d.Trim().ToLowerInvariant()))
        {
            if (host == baseDomain)
                return allowFallback && !string.IsNullOrWhiteSpace(options.FallbackSlug) ? options.FallbackSlug : null;

            if (host.EndsWith("." + baseDomain, StringComparison.Ordinal))
            {
                var slug = host[..^(baseDomain.Length + 1)];
                return Tenant.IsValidSlug(slug) ? slug : null; // "a.b.howly.app" is not a portal
            }
        }

        return null;
    }

    private async Task<Tenant?> FindAsync(string slug, ITenantRepository tenants, CancellationToken cancellationToken)
    {
        if (cache.TryGet(slug, out var cached))
            return cached;

        var tenant = await tenants.FindBySlugAsync(slug, cancellationToken);
        // Unknown slugs are cached too (briefly), so a flood of random subdomains does not reach the database every time.
        cache.Set(slug, tenant);
        return tenant;
    }

    private static async Task RefuseAsync(HttpContext context, IProblemDetailsService problemDetails, int status, string title, string code)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        await problemDetails.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
