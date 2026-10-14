using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>
/// Failures are deliberately vague: callers must not reveal why a login or refresh failed. The only exception is an
/// inactive account, reported after the password was verified, so the customer knows to contact the company.
/// </summary>
public interface IAuthService
{
    Task<LoginResult> LoginCustomerAsync(CustomerLoginRequest request, CancellationToken cancellationToken = default);
    Task<LoginResult> LoginInternalAsync(InternalLoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ChangePasswordOutcome> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
}
