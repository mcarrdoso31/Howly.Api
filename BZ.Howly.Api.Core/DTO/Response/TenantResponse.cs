namespace BZ.Howly.Api.Core.DTO.Response;

/// <param name="IsPlatform">The platform owners' portal (no customers nor processes there).</param>
/// <param name="LogoVersion">Changes whenever the logo changes; null without one (GET /api/tenant/logo?v=…).</param>
public sealed record TenantResponse(string Slug, string Name, bool IsPlatform, string? LogoVersion);
