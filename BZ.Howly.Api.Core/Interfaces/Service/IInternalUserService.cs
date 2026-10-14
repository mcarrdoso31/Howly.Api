using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>Manages company staff accounts (Analyst and Admin). Customer accounts are out of reach here.</summary>
public interface IInternalUserService
{
    Task<OperationResult<InternalUserCreatedResponse>> CreateAsync(CreateInternalUserRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<InternalUserListItemResponse>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);
    /// <summary>The active staff as options (e.g. to pick a process assignee).</summary>
    Task<IReadOnlyList<PersonResponse>> ListActiveStaffAsync(CancellationToken cancellationToken = default);

    Task<OperationResult<InternalUserResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OperationResult<TemporaryPasswordResponse>> ResetPasswordAsync(Guid id, Guid requestedBy, CancellationToken cancellationToken = default);

    /// <summary>Renames and changes the role; the last active Admin cannot stop being Admin.</summary>
    Task<OperationResult<InternalUserResponse>> UpdateAsync(Guid id, UpdateInternalUserRequest request, Guid requestedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the access (sessions included). Open processes go to <see cref="DeactivateInternalUserRequest.ReassignTo"/> first;
    /// nobody deactivates themselves.
    /// </summary>
    Task<OperationResult<InternalUserResponse>> DeactivateAsync(Guid id, DeactivateInternalUserRequest request, Guid requestedBy, CancellationToken cancellationToken = default);

    Task<OperationResult<InternalUserResponse>> ReactivateAsync(Guid id, Guid requestedBy, CancellationToken cancellationToken = default);
}
