namespace BZ.Howly.Api.Core.DTO.Response;

/// <param name="MustChangePassword">True while the user still has the temporary password set by the staff.</param>
/// <param name="MustConfirmProfile">True while a new customer has not confirmed their data (asked after the password change).</param>
/// <param name="AvatarVersion">Null without a photo/logo; otherwise a value that changes with every new image.</param>
public sealed record UserResponse(
    Guid Id, string Name, IReadOnlyList<string> Roles, bool MustChangePassword, bool MustConfirmProfile,
    string? Nickname, string? AvatarVersion);
