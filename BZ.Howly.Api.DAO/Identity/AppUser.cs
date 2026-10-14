using Microsoft.AspNetCore.Identity;

namespace BZ.Howly.Api.DAO.Identity;

/// <summary>
/// Identity user. Customers use their CNPJ (normalized) as UserName; internal staff use a plain username.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Set when the staff creates the account or resets its password; cleared once the user picks a new one.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Set for new customers, cleared once they confirm their data on first access.</summary>
    public bool MustConfirmProfile { get; set; }

    /// <summary>Short name used in greetings and menus.</summary>
    public string? Nickname { get; set; }

    /// <summary>Backup email of a staff member (the main one is Identity's Email).</summary>
    public string? SecondaryEmail { get; set; }

    /// <summary>Backup phone of a staff member, digits only (the main one is Identity's PhoneNumber).</summary>
    public string? SecondaryPhone { get; set; }

    /// <summary>File storage key of the photo (staff) or logo (customer).</summary>
    public string? AvatarKey { get; set; }

    public DateTimeOffset? AvatarUpdatedAt { get; set; }

    /// <summary>False when the staff deactivated the account: no sign in, no session refresh.</summary>
    public bool IsActive { get; set; } = true;
}
