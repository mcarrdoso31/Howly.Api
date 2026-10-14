using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;

namespace BZ.Howly.Api.Tests.Fakes;

/// <summary>
/// In-memory user store. Lockout and password-strength rules are not simulated beyond a minimum length;
/// the real rules are covered by the integration tests.
/// </summary>
public sealed class FakeUserAccountStore : IUserAccountStore
{
    public const int MinimumPasswordLength = 8;

    private readonly Dictionary<Guid, (UserAccount Account, string Password)> _users = new();
    private readonly HashSet<Guid> _lockedOut = new();

    public UserAccount Add(string username, string password, params string[] roles) =>
        Store(new UserAccount(Guid.NewGuid(), username, $"{username} name", roles), password);

    public UserAccount AddWithTemporaryPassword(string username, string password, params string[] roles) =>
        Store(new UserAccount(Guid.NewGuid(), username, $"{username} name", roles, MustChangePassword: true), password);

    public void LockOut(Guid userId) => _lockedOut.Add(userId);

    public void Remove(Guid userId) => _users.Remove(userId);

    public string PasswordOf(Guid userId) => _users[userId].Password;

    public Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values
            .Select(u => u.Account)
            .FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)));

    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.TryGetValue(userId, out var user) ? user.Account : null);

    public Task<(IReadOnlyList<UserAccount> Items, int TotalCount)> SearchAsync(
        IReadOnlyList<string> roles, string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        var matches = _users.Values
            .Select(u => u.Account)
            .Where(a => a.HasAnyRole(roles))
            .Where(a => string.IsNullOrWhiteSpace(search)
                || a.Username.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || a.DisplayName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.DisplayName)
            .ToList();
        return Task.FromResult<(IReadOnlyList<UserAccount>, int)>((matches.Skip(skip).Take(take).ToList(), matches.Count));
    }

    public Task<IReadOnlyList<UserAccount>> ListActiveAsync(IReadOnlyList<string> roles, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserAccount>>(
            _users.Values.Select(u => u.Account).Where(a => a.IsActive && a.HasAnyRole(roles)).OrderBy(a => a.DisplayName).ToList());

    public Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        if (_lockedOut.Contains(userId))
            return Task.FromResult(PasswordCheckResult.LockedOut);

        return Task.FromResult(_users.TryGetValue(userId, out var user) && user.Password == password
            ? PasswordCheckResult.Success
            : PasswordCheckResult.Invalid);
    }

    public async Task<UserCreationResult> CreateAsync(NewUserAccount user, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        if (await FindByUsernameAsync(user.Username, cancellationToken) is not null)
            return UserCreationResult.Taken;

        var account = Store(new UserAccount(Guid.NewGuid(), user.Username, user.DisplayName, [user.Role],
            MustChangePassword: true, MustConfirmProfile: user.MustConfirmProfile), temporaryPassword);
        return UserCreationResult.Created(account);
    }

    public Task<PasswordChangeResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(userId, out var user) || user.Password != currentPassword)
            return Task.FromResult(PasswordChangeResult.InvalidCurrentPassword);

        if (newPassword.Length < MinimumPasswordLength)
            return Task.FromResult(PasswordChangeResult.Rejected("Passwords must be at least 8 characters."));

        Store(user.Account with { MustChangePassword = false }, newPassword);
        return Task.FromResult(PasswordChangeResult.Success);
    }

    public Task<bool> ResetPasswordAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(userId, out var user))
            return Task.FromResult(false);

        _lockedOut.Remove(userId);
        Store(user.Account with { MustChangePassword = true }, temporaryPassword);
        return Task.FromResult(true);
    }

    public Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(userId, out var user))
            return Task.FromResult(false);

        Store(user.Account with { IsActive = isActive }, user.Password);
        return Task.FromResult(true);
    }

    public Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.TryGetValue(userId, out var user)
            ? new AccountProfile(user.Account.Id, user.Account.Username, user.Account.DisplayName, user.Account.Roles,
                user.Account.Nickname, null, null, null, null, null, null)
            : null);

    public Task<bool> UpdateStaffProfileAsync(Guid userId, StaffProfileChanges changes, CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(userId, out var user))
            return Task.FromResult(false);

        Store(user.Account with { DisplayName = changes.DisplayName!, Nickname = changes.Nickname }, user.Password);
        return Task.FromResult(true);
    }

    public Task<bool> SetAvatarAsync(Guid userId, string? avatarKey, DateTimeOffset? updatedAt, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.ContainsKey(userId));

    public Task<bool> CompleteProfileConfirmationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(userId, out var user))
            return Task.FromResult(false);

        Store(user.Account with { MustConfirmProfile = false }, user.Password);
        return Task.FromResult(true);
    }

    public Task<bool> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(userId, out var user))
            return Task.FromResult(false);

        Store(user.Account with { DisplayName = displayName }, user.Password);
        return Task.FromResult(true);
    }

    public Task<bool> SetInternalRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        if (!_users.TryGetValue(userId, out var user))
            return Task.FromResult(false);

        var roles = user.Account.Roles.Where(r => !Roles.Internal.Contains(r)).Append(role).ToList();
        Store(user.Account with { Roles = roles }, user.Password);
        return Task.FromResult(true);
    }

    public Task<int> CountActiveInRoleAsync(string role, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.Count(u => u.Account.IsActive && u.Account.Roles.Contains(role)));

    private UserAccount Store(UserAccount account, string password)
    {
        _users[account.Id] = (account, password);
        return account;
    }
}
