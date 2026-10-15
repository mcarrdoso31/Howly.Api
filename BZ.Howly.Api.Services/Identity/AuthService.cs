using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Core.Shared.Exceptions;

namespace BZ.Howly.Api.Services.Identity;

public sealed class AuthService(
    IUserAccountStore userAccountStore,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    TimeProvider timeProvider) : IAuthService
{
    private static readonly string[] CustomerRoles = [Roles.Customer];
    private static readonly string[] PlatformRoles = [Roles.PlatformAdmin];

    public async Task<LoginResult> LoginCustomerAsync(CustomerLoginRequest request, CancellationToken cancellationToken = default)
    {
        if (!TaxId.TryParse(request.Document, out var document))
            return LoginResult.InvalidCredentials;

        return await LoginAsync(document!.Value, request.Password, CustomerRoles, cancellationToken);
    }

    public Task<LoginResult> LoginInternalAsync(InternalLoginRequest request, CancellationToken cancellationToken = default) =>
        LoginAsync(request.Username?.Trim() ?? string.Empty, request.Password, Roles.Internal, cancellationToken);

    public Task<LoginResult> LoginPlatformAsync(InternalLoginRequest request, CancellationToken cancellationToken = default) =>
        LoginAsync(request.Username?.Trim() ?? string.Empty, request.Password, PlatformRoles, cancellationToken);

    public async Task<AuthSession?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var stored = await refreshTokenRepository.FindByHashAsync(tokenService.HashRefreshToken(refreshToken), cancellationToken);
        if (stored is null)
            return null;

        if (stored.IsRevoked)
        {
            // A revoked token being presented again means it may have been stolen: kill the whole chain.
            await refreshTokenRepository.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            await refreshTokenRepository.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (!stored.IsActive(now))
            return null;

        var user = await userAccountStore.FindByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return null;

        return await IssueSessionAsync(user, stored, cancellationToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var stored = await refreshTokenRepository.FindByHashAsync(tokenService.HashRefreshToken(refreshToken), cancellationToken);
        if (stored is null)
            return;

        stored.Revoke(timeProvider.GetUtcNow());
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userAccountStore.FindByIdAsync(userId, cancellationToken);
        return user is null ? null : ToResponse(user);
    }

    public async Task<ChangePasswordOutcome> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (request.NewPassword == request.CurrentPassword)
            return new ChangePasswordOutcome(PasswordChangeStatus.Rejected, ["The new password must be different from the current one."], null);

        var result = await userAccountStore.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken);
        if (result.Status != PasswordChangeStatus.Success)
            return new ChangePasswordOutcome(result.Status, result.Errors, null);

        var user = await userAccountStore.FindByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException("The signed-in user no longer exists.");

        // Every session opened with the old password ends here; only the one returned below stays valid.
        await refreshTokenRepository.RevokeAllForUserAsync(userId, timeProvider.GetUtcNow(), cancellationToken);

        var session = await IssueSessionAsync(user, previous: null, cancellationToken);
        return new ChangePasswordOutcome(PasswordChangeStatus.Success, [], session);
    }

    private async Task<LoginResult> LoginAsync(
        string username, string password, IReadOnlyList<string> allowedRoles, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return LoginResult.InvalidCredentials;

        var user = await userAccountStore.FindByUsernameAsync(username, cancellationToken);
        if (user is null || !user.HasAnyRole(allowedRoles))
            return LoginResult.InvalidCredentials;

        var passwordCheck = await userAccountStore.CheckPasswordAsync(user.Id, password, cancellationToken);
        if (passwordCheck != PasswordCheckResult.Success)
            return LoginResult.InvalidCredentials;

        // Checked only after the password, so an inactive account is not revealed to someone guessing CPFs or CNPJs.
        if (!user.IsActive)
            return LoginResult.AccountInactive;

        return LoginResult.Success(await IssueSessionAsync(user, previous: null, cancellationToken));
    }

    private async Task<AuthSession> IssueSessionAsync(UserAccount user, RefreshToken? previous, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var accessToken = tokenService.CreateAccessToken(user);
        var issuedRefresh = tokenService.CreateRefreshToken();

        var refreshToken = RefreshToken.Issue(user.Id, issuedRefresh, now, previous?.FamilyId);
        previous?.Revoke(now, refreshToken.Id);

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        var response = new LoginResponse(accessToken.Value, accessToken.ExpiresAt, ToResponse(user));
        return new AuthSession(response, issuedRefresh.Value, issuedRefresh.ExpiresAt);
    }

    private static UserResponse ToResponse(UserAccount user) =>
        new(user.Id, user.DisplayName, user.Roles, user.MustChangePassword, user.MustConfirmProfile, user.Nickname, user.AvatarVersion);
}
