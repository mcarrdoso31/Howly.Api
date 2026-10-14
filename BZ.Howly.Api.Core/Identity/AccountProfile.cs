namespace BZ.Howly.Api.Core.Identity;

/// <summary>
/// Personal data of an account, as the person sees it on their profile page. Email and phones are filled only by
/// staff members: customers keep their contacts on the company.
/// </summary>
public sealed record AccountProfile(
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string? Nickname,
    string? Email,
    string? Phone,
    string? SecondaryEmail,
    string? SecondaryPhone,
    string? AvatarKey,
    DateTimeOffset? AvatarUpdatedAt)
{
    public string? AvatarVersion => AvatarUpdatedAt?.ToUnixTimeMilliseconds().ToString();
}
