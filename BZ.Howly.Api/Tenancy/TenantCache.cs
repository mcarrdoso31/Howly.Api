using BZ.Howly.Api.Core.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BZ.Howly.Api.Tenancy;

/// <summary>
/// The per-instance cache of portals: bounded in size (each entry counts 1, at most
/// <see cref="TenancyOptions.MaxCachedPortals"/>) and kept apart from any other cache, since its keys come from clients.
/// </summary>
public sealed class TenantCache(IOptions<TenancyOptions> options) : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = options.Value.MaxCachedPortals });

    public bool TryGet(string slug, out Tenant? tenant) => _cache.TryGetValue(slug, out tenant);

    public void Set(string slug, Tenant? tenant)
    {
        var duration = tenant is null ? options.Value.NotFoundCacheDuration : options.Value.CacheDuration;
        if (duration > TimeSpan.Zero)
            _cache.Set(slug, tenant, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = duration });
    }

    /// <summary>Drops a portal, so the next request reads it again (e.g. after its name or logo changed).</summary>
    public void Forget(string slug) => _cache.Remove(slug);

    public void Dispose() => _cache.Dispose();
}
