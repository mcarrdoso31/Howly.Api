namespace BZ.Howly.Api.Core.Shared;

/// <summary>
/// Brazilian phone numbers with area code: (11) 91234-5678 (mobile, 11 digits) or (11) 3456-7890 (landline, 10 digits).
/// Stored as digits only.
/// </summary>
public static class BrazilianPhone
{
    public static bool TryNormalize(string? value, out string digits)
    {
        digits = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Only digits and the usual mask characters are accepted, so "+55" or letters are rejected.
        if (value.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '(' or ')' or '-')))
            return false;

        var candidate = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (candidate.Length is not (10 or 11))
            return false;

        // Area codes go from 11 to 99 and never end with 0.
        if (candidate[0] == '0' || candidate[1] == '0')
            return false;

        // Eleven digits means a mobile number, which always starts with 9.
        if (candidate.Length == 11 && candidate[2] != '9')
            return false;

        digits = candidate;
        return true;
    }
}
