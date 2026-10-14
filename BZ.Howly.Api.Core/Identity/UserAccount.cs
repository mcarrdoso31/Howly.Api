namespace BZ.Howly.Api.Core.Identity;

/// <param name="IsActive">False when the staff deactivated the account: it cannot sign in or refresh a session.</param>
/// <param name="MustConfirmProfile">True for a new customer until they review and confirm their data.</param>
/// <param name="AvatarVersion">Changes whenever the photo/logo changes (clients use it to refresh their copy); null without one.</param>
/// <param name="TenantId">The consultancy the account belongs to (accounts never cross consultancies).</param>
public sealed record UserAccount(
    Guid Id, string Username, string DisplayName, IReadOnlyList<string> Roles,
    bool MustChangePassword = false, bool IsActive = true, bool MustConfirmProfile = false,
    string? Nickname = null, string? AvatarVersion = null, Guid TenantId = default)
{
    public bool HasAnyRole(IEnumerable<string> roles) => roles.Any(Roles.Contains);
}
