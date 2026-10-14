namespace BZ.Howly.Api.Core.DTO.Response;

/// <param name="TemporaryPassword">Shown only in this response. It is never stored or returned again.</param>
public sealed record InternalUserCreatedResponse(Guid Id, string Username, string DisplayName, string Role, string TemporaryPassword);

public sealed record InternalUserListItemResponse(Guid Id, string Username, string DisplayName, IReadOnlyList<string> Roles, bool MustChangePassword, bool IsActive);

/// <param name="OpenProcessCount">In-progress or paused processes under this person; they must be handed over before deactivating.</param>
public sealed record InternalUserResponse(
    Guid Id, string Username, string DisplayName, IReadOnlyList<string> Roles, bool MustChangePassword, bool IsActive, int OpenProcessCount);
