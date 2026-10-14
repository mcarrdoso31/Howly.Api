namespace BZ.Howly.Api.Authorization;

public sealed class RefreshCookieOptions
{
    public const string SectionName = "Auth:RefreshCookie";

    public string Name { get; set; } = "howly_refresh";

    /// <summary>
    /// Strict when web and API share the same site (e.g. localhost).
    /// Use None when the web app is hosted on a different domain than the API.
    /// </summary>
    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;

    public const string Path = "/api/auth";
}
