namespace BZ.Howly.Api.Tenancy;

/// <summary>How a request finds its consultancy: <c>{slug}.{base domain}</c>.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Domains the portals live under (e.g. "howly.app", and "localhost" for development).</summary>
    public string[] BaseDomains { get; set; } = ["localhost"];

    /// <summary>
    /// The consultancy of a bare base domain (no subdomain), for development and tests only. Empty in production,
    /// where a bare domain is not a portal.
    /// </summary>
    public string? FallbackSlug { get; set; }

    /// <summary>How long a consultancy stays cached per instance (a suspension takes at most this long to apply everywhere).</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(2);
}
