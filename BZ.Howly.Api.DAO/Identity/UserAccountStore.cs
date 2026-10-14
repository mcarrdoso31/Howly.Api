using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.DAO.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Identity;

/// <summary>Adapts ASP.NET Core Identity to the domain's <see cref="IUserAccountStore"/>.</summary>
public sealed class UserAccountStore(UserManager<AppUser> userManager, HowlyDbContext context) : IUserAccountStore
{
    private const string PasswordMismatch = "PasswordMismatch";
    private const string DuplicateUserName = "DuplicateUserName";

    public async Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(username);
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<(IReadOnlyList<UserAccount> Items, int TotalCount)> SearchAsync(
        IReadOnlyList<string> roles, string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        var normalizedRoles = roles.Select(r => r.ToUpperInvariant()).ToList();
        var userIdsInRoles =
            from userRole in context.UserRoles
            join role in context.Roles on userRole.RoleId equals role.Id
            where normalizedRoles.Contains(role.NormalizedName!)
            select userRole.UserId;

        var query = context.Users.Where(u => userIdsInRoles.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ToUpper on both sides keeps the search case-insensitive on SQL Server, SQLite and PostgreSQL alike.
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(u => u.NormalizedUserName!.Contains(term) || u.DisplayName.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var users = await query.OrderBy(u => u.DisplayName).Skip(skip).Take(take).ToListAsync(cancellationToken);

        var userIds = users.Select(u => u.Id).ToList();
        var rolesByUser = (await (
                from userRole in context.UserRoles
                join role in context.Roles on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId)
                select new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken))
            .ToLookup(x => x.UserId, x => x.Name!);

        var items = users
            .Select(u => ToAccount(u, rolesByUser[u.Id].ToList()))
            .ToList();

        return (items, total);
    }

    public async Task<IReadOnlyList<UserAccount>> ListActiveAsync(IReadOnlyList<string> roles, CancellationToken cancellationToken = default)
    {
        var normalizedRoles = roles.Select(r => r.ToUpperInvariant()).ToList();
        var rows = await (
                from user in context.Users
                join userRole in context.UserRoles on user.Id equals userRole.UserId
                join role in context.Roles on userRole.RoleId equals role.Id
                where user.IsActive && normalizedRoles.Contains(role.NormalizedName!)
                select new { User = user, Role = role.Name! })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.User.Id)
            .Select(g => ToAccount(g.First().User, g.Select(r => r.Role).ToList()))
            .OrderBy(a => a.DisplayName)
            .ThenBy(a => a.Id)
            .ToList();
    }

    public async Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return PasswordCheckResult.Invalid;

        if (await userManager.IsLockedOutAsync(user))
            return PasswordCheckResult.LockedOut;

        if (await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.ResetAccessFailedCountAsync(user);
            return PasswordCheckResult.Success;
        }

        await userManager.AccessFailedAsync(user);
        return await userManager.IsLockedOutAsync(user) ? PasswordCheckResult.LockedOut : PasswordCheckResult.Invalid;
    }

    public async Task<UserCreationResult> CreateAsync(NewUserAccount newUser, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        var user = new AppUser
        {
            UserName = newUser.Username,
            DisplayName = newUser.DisplayName,
            MustChangePassword = true,
            MustConfirmProfile = newUser.MustConfirmProfile,
        };

        var created = await userManager.CreateAsync(user, temporaryPassword);
        if (!created.Succeeded)
        {
            return created.Errors.Any(e => e.Code == DuplicateUserName)
                ? UserCreationResult.Taken
                : UserCreationResult.Failed(created.Errors.Select(e => e.Description).ToList());
        }

        var roleAdded = await userManager.AddToRoleAsync(user, newUser.Role);
        if (!roleAdded.Succeeded)
            return UserCreationResult.Failed(roleAdded.Errors.Select(e => e.Description).ToList());

        return UserCreationResult.Created(await ToAccountAsync(user));
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return PasswordChangeResult.InvalidCurrentPassword;

        // Saved together with the new password hash by ChangePasswordAsync.
        var mustChangeBefore = user.MustChangePassword;
        user.MustChangePassword = false;

        var changed = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (changed.Succeeded)
            return PasswordChangeResult.Success;

        user.MustChangePassword = mustChangeBefore;
        return changed.Errors.Any(e => e.Code == PasswordMismatch)
            ? PasswordChangeResult.InvalidCurrentPassword
            : PasswordChangeResult.Rejected(changed.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<bool> ResetPasswordAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        user.PasswordHash = userManager.PasswordHasher.HashPassword(user, temporaryPassword);
        user.MustChangePassword = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;

        // Persists every change above in a single update.
        var updated = await userManager.UpdateSecurityStampAsync(user);
        if (!updated.Succeeded)
            throw new InvalidOperationException($"Could not reset password: {string.Join("; ", updated.Errors.Select(e => e.Description))}");

        return true;
    }

    public async Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        if (user.IsActive == isActive)
            return true;

        user.IsActive = isActive;
        await UpdateAsync(user);
        return true;
    }

    public async Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        var roles = await userManager.GetRolesAsync(user);
        return new AccountProfile(
            user.Id, user.UserName!, user.DisplayName, roles.ToList(), user.Nickname, user.Email, user.PhoneNumber,
            user.SecondaryEmail, user.SecondaryPhone, user.AvatarKey, user.AvatarUpdatedAt);
    }

    public async Task<bool> UpdateStaffProfileAsync(Guid userId, StaffProfileChanges changes, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        user.DisplayName = changes.DisplayName!;
        user.Nickname = changes.Nickname;
        user.Email = changes.Email;
        user.PhoneNumber = changes.Phone;
        user.SecondaryEmail = changes.SecondaryEmail;
        user.SecondaryPhone = changes.SecondaryPhone;
        await UpdateAsync(user); // also refreshes NormalizedEmail
        return true;
    }

    public async Task<bool> SetAvatarAsync(Guid userId, string? avatarKey, DateTimeOffset? updatedAt, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        user.AvatarKey = avatarKey;
        user.AvatarUpdatedAt = avatarKey is null ? null : updatedAt;
        await UpdateAsync(user);
        return true;
    }

    public async Task<bool> CompleteProfileConfirmationAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        if (user.MustConfirmProfile)
        {
            user.MustConfirmProfile = false;
            await UpdateAsync(user);
        }

        return true;
    }

    public async Task<bool> UpdateDisplayNameAsync(Guid userId, string displayName, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        user.DisplayName = displayName;
        await UpdateAsync(user);
        return true;
    }

    public async Task<bool> SetInternalRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        var current = await userManager.GetRolesAsync(user);
        var toRemove = current.Where(r => Roles.Internal.Contains(r) && r != role).ToList();
        if (toRemove.Count > 0)
            Check(await userManager.RemoveFromRolesAsync(user, toRemove));
        if (!current.Contains(role))
            Check(await userManager.AddToRoleAsync(user, role));
        return true;
    }

    public async Task<int> CountActiveInRoleAsync(string role, CancellationToken cancellationToken = default) =>
        (await userManager.GetUsersInRoleAsync(role)).Count(u => u.IsActive);

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not change the user roles: {string.Join("; ", result.Errors.Select(e => e.Description))}");
    }

    private async Task UpdateAsync(AppUser user)
    {
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            throw new InvalidOperationException($"Could not update user: {string.Join("; ", updated.Errors.Select(e => e.Description))}");
    }

    private async Task<UserAccount> ToAccountAsync(AppUser user) => ToAccount(user, (await userManager.GetRolesAsync(user)).ToList());

    private static UserAccount ToAccount(AppUser user, IReadOnlyList<string> roles) => new(
        user.Id, user.UserName!, user.DisplayName, roles, user.MustChangePassword, user.IsActive, user.MustConfirmProfile,
        user.Nickname, user.AvatarUpdatedAt?.ToUnixTimeMilliseconds().ToString(), user.TenantId);
}
