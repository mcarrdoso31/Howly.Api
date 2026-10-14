using System.Net.Mail;

namespace BZ.Howly.Api.Core.Shared;

public static class EmailAddress
{
    public const int MaxLength = 254;

    /// <summary>A single plain address (no display name), at most 254 characters. Surrounding spaces are ignored.</summary>
    public static bool IsValid(string? value)
    {
        var email = value?.Trim();
        return !string.IsNullOrEmpty(email)
            && email.Length <= MaxLength
            && MailAddress.TryCreate(email, out var address)
            && address.Address == email;
    }
}
