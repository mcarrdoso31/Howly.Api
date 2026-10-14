namespace BZ.Howly.Api.Core.Identity;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>A freshly generated refresh token. Only <see cref="Hash"/> is ever persisted.</summary>
public sealed record IssuedRefreshToken(string Value, string Hash, DateTimeOffset ExpiresAt);
