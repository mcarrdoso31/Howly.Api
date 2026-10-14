namespace BZ.Howly.Api.Core.DTO.Response;

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);
