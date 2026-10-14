namespace BZ.Howly.Api.Core.DTO.Response;

/// <param name="IsPlatform">The platform owners' portal (no customers nor processes there).</param>
public sealed record TenantResponse(string Slug, string Name, bool IsPlatform);
