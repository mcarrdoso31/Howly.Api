namespace BZ.Howly.Api.Core.Shared;

/// <summary>
/// Brazilian company registration number. Supports both the numeric format and the
/// alphanumeric format introduced by Receita Federal in July 2026 (IN RFB 2.229/2024):
/// 12 alphanumeric characters followed by 2 numeric check digits.
/// </summary>
public sealed record Cnpj
{
    private const int Length = 14;
    private static readonly int[] FirstCheckWeights = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] SecondCheckWeights = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public string Value { get; }

    private Cnpj(string value) => Value = value;

    public static bool TryParse(string? input, out Cnpj? cnpj)
    {
        cnpj = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var normalized = Normalize(input);
        if (!HasValidShape(normalized) || normalized.Distinct().Count() == 1)
            return false;

        if (CheckDigit(normalized[..12], FirstCheckWeights) != normalized[12] - '0' ||
            CheckDigit(normalized[..13], SecondCheckWeights) != normalized[13] - '0')
            return false;

        cnpj = new Cnpj(normalized);
        return true;
    }

    public static Cnpj Parse(string input) =>
        TryParse(input, out var cnpj) ? cnpj! : throw new FormatException($"'{input}' is not a valid CNPJ.");

    public override string ToString() => Value;

    private static string Normalize(string input) =>
        new(input.Where(c => c is not ('.' or '/' or '-') && !char.IsWhiteSpace(c))
                 .Select(char.ToUpperInvariant)
                 .ToArray());

    private static bool HasValidShape(string value) =>
        value.Length == Length &&
        value[..12].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)) &&
        value[12..].All(char.IsAsciiDigit);

    // Each character is worth its ASCII code minus 48, so '0'-'9' => 0-9 and 'A'-'Z' => 17-42.
    private static int CheckDigit(string body, int[] weights)
    {
        var sum = body.Select((c, i) => (c - '0') * weights[i]).Sum();
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
