using BZ.Howly.Api.Core.Identity;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IUserAccountStore
{
    Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accounts with any of <paramref name="roles"/>, filtered by username or display name (partial, case-insensitive),
    /// ordered by display name.
    /// </summary>
    Task<(IReadOnlyList<UserAccount> Items, int TotalCount)> SearchAsync(
        IReadOnlyList<string> roles, string? search, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Every active account with any of <paramref name="roles"/>, ordered by display name.</summary>
    Task<IReadOnlyList<UserAccount>> ListActiveAsync(IReadOnlyList<string> roles, CancellationToken cancellationToken = default);

    /// <summary>Checks the password and applies lockout rules (failed attempts count, lockout window).</summary>
    Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    /// <summary>Creates the user with <paramref name="temporaryPassword"/> and flags it to change the password on first access.</summary>
    Task<UserCreationResult> CreateAsync(NewUserAccount user, string temporaryPassword, CancellationToken cancellationToken = default);

    /// <summary>Changes the password and clears the must-change flag.</summary>
    Task<PasswordChangeResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Replaces the password with a temporary one, flags it to be changed and clears any lockout. False when the user does not exist.</summary>
    Task<bool> ResetPasswordAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken = default);

    /// <summary>Activates or deactivates the account. False when the user does not exist.</summary>
    Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default);

    Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Saves the normalized changes of a staff member's own profile. False when the user does not exist.</summary>
    Task<bool> UpdateStaffProfileAsync(Guid userId, StaffProfileChanges changes, CancellationToken cancellationToken = default);

    /// <summary>Points the account to a stored photo/logo, or to none (null key). False when the user does not exist.</summary>
    Task<bool> SetAvatarAsync(Guid userId, string? avatarKey, DateTimeOffset? updatedAt, CancellationToken cancellationToken = default);

    /// <summary>Clears the flag that asks a new customer to confirm their data. False when the user does not exist.</summary>
    Task<bool> CompleteProfileConfirmationAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Renames the account (the name shown after sign in). False when the user does not exist.</summary>
    Task<bool> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Replaces the staff role (Analyst or Admin) of the account. False when the user does not exist.</summary>
    Task<bool> SetInternalRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default);

    /// <summary>How many active accounts have <paramref name="role"/>.</summary>
    Task<int> CountActiveInRoleAsync(string role, CancellationToken cancellationToken = default);
}
