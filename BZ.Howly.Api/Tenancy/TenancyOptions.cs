namespace BZ.Howly.Api.Tenancy;

/// <summary>How a request finds its consultancy: <c>{slug}.{base domain}</c>.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Domains the portals live under (e.g. "howly.app", and "localhost" for development).</summary>
    public string[] BaseDomains { get; set; } = ["localhost"];

    /// <summary>
    /// The consultancy of a bare base domain (no subdomain). Honoured only in Development and in the tests: in
    /// production a bare domain is never a portal, whatever this says.
    /// </summary>
    public string? FallbackSlug { get; set; }

    /// <summary>How long a consultancy stays cached per instance (a suspension takes at most this long to apply everywhere).</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long "no such portal" is remembered: short, so a portal just created opens almost at once, yet enough to
    /// spare the database from a flood of random subdomains.
    /// </summary>
    public TimeSpan NotFoundCacheDuration { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Most portals (found or not) kept in the cache: random hosts cannot grow it without limit.</summary>
    public int MaxCachedPortals { get; set; } = 10_000;
}
