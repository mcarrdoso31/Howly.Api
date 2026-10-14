namespace BZ.Howly.Api.Core.DTO.Response;

/// <param name="Phone">Digits only; clients apply the mask. Email and phones are null for customers.</param>
public sealed record AccountProfileResponse(
    Guid Id,
    string Username,
    string DisplayName,
    string? Nickname,
    string? Email,
    string? Phone,
    string? SecondaryEmail,
    string? SecondaryPhone,
    string? AvatarVersion);
