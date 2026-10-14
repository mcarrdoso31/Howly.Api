using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Identity;

/// <summary>
/// What a staff member edits on their own profile. Only the display name is required; phones are Brazilian numbers
/// (stored as digits) and the secondary email/phone are a backup of the main ones.
/// </summary>
public sealed record StaffProfileChanges(
    string? DisplayName, string? Nickname, string? Email, string? Phone, string? SecondaryEmail, string? SecondaryPhone)
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxNicknameLength = 50;

    public static IReadOnlyDictionary<string, string[]> Validate(StaffProfileChanges changes)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(changes.DisplayName) || changes.DisplayName.Trim().Length > MaxDisplayNameLength)
            errors["displayName"] = [$"Name is required and must have at most {MaxDisplayNameLength} characters."];

        if (changes.Nickname?.Trim().Length > MaxNicknameLength)
            errors["nickname"] = [$"Nickname must have at most {MaxNicknameLength} characters."];

        CheckEmail(errors, "email", changes.Email);
        CheckEmail(errors, "secondaryEmail", changes.SecondaryEmail);
        CheckPhone(errors, "phone", changes.Phone);
        CheckPhone(errors, "secondaryPhone", changes.SecondaryPhone);

        return errors;
    }

    /// <summary>Trimmed values, blanks as null and phones as digits. Call only on valid changes.</summary>
    public StaffProfileChanges Normalize() => new(
        DisplayName!.Trim(), Blank(Nickname), Blank(Email), PhoneDigits(Phone), Blank(SecondaryEmail), PhoneDigits(SecondaryPhone));

    private static void CheckEmail(Dictionary<string, string[]> errors, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !EmailAddress.IsValid(value))
            errors[field] = ["Email must be a valid email address."];
    }

    private static void CheckPhone(Dictionary<string, string[]> errors, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !BrazilianPhone.TryNormalize(value, out _))
            errors[field] = ["Phone must be a Brazilian number with area code."];
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? PhoneDigits(string? value) => BrazilianPhone.TryNormalize(value, out var digits) ? digits : null;
}
