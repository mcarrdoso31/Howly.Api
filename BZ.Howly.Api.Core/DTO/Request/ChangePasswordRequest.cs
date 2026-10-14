namespace BZ.Howly.Api.Core.DTO.Request;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
