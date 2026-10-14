using BZ.Howly.Api.Core.DTO.Response;

namespace BZ.Howly.Api.Core.Identity;

/// <summary>Result of a successful login or refresh. The refresh token goes to an httpOnly cookie, never to the body.</summary>
public sealed record AuthSession(LoginResponse Response, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
