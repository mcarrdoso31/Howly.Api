using System.Text.RegularExpressions;

namespace BZ.Howly.Api.Core.Tenancy;

public enum TenantStatus
{
    Active,

    /// <summary>The portal answers 403: nobody signs in until it is reactivated.</summary>
    Suspended,
}

/// <summary>
/// A consultancy using Howly ("tenant"). It owns every customer, process and account created in its portal, reached
/// at its own subdomain (<c>{slug}.howly.app</c>). The special platform tenant (<see cref="PlatformSlug"/>) hosts only
/// the platform owners.
/// </summary>
public sealed partial class Tenant
{
    public const int MaxSlugLength = 40;
    public const int MaxNameLength = 120;

    /// <summary>The platform owners' portal (future super admin panel).</summary>
    public const string PlatformSlug = "admin";

    /// <summary>Subdomains no consultancy can take.</summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string> { PlatformSlug, "api", "www", "app" };

    public Guid Id { get; private set; }

    /// <summary>The subdomain: lowercase letters, digits and hyphens.</summary>
    public string Slug { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; }

    /// <summary>The platform tenant: only platform owners sign in there and business routes do not exist.</summary>
    public bool IsPlatform { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private Tenant() { }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    public static bool IsValidSlug(string? slug) => slug is not null && SlugPattern().IsMatch(slug);

    /// <summary>A consultancy portal. Reserved and malformed slugs are refused.</summary>
    public static Tenant Create(Guid id, string slug, string name, DateTimeOffset now) =>
        New(id, slug, name, isPlatform: false, now);

    /// <summary>The platform owners' portal, at <see cref="PlatformSlug"/>.</summary>
    public static Tenant CreatePlatform(Guid id, string name, DateTimeOffset now) => New(id, PlatformSlug, name, isPlatform: true, now);

    public void Suspend() => Status = TenantStatus.Suspended;

    public void Reactivate() => Status = TenantStatus.Active;

    private static Tenant New(Guid id, string slug, string name, bool isPlatform, DateTimeOffset now)
    {
        if (!IsValidSlug(slug))
            throw new ArgumentException("The subdomain must have lowercase letters, digits and hyphens only.", nameof(slug));
        if (!isPlatform && ReservedSlugs.Contains(slug))
            throw new ArgumentException($"The subdomain \"{slug}\" is reserved.", nameof(slug));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"The name is required and must have at most {MaxNameLength} characters.", nameof(name));

        return new Tenant { Id = id, Slug = slug, Name = name.Trim(), Status = TenantStatus.Active, IsPlatform = isPlatform, CreatedAt = now };
    }
}
