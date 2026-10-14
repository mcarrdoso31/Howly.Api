namespace BZ.Howly.Api.Core.DTO.Request;

/// <summary>A staff member's own profile. Only the display name is required.</summary>
public sealed record UpdateStaffProfileRequest(
    string? DisplayName, string? Nickname, string? Email, string? Phone, string? SecondaryEmail, string? SecondaryPhone);
