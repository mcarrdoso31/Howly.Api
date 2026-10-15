namespace BZ.Howly.Api.Core.Shared;

public enum TaxIdKind
{
    /// <summary>A person (Cadastro de Pessoas Físicas): 11 digits.</summary>
    Cpf,

    /// <summary>A company (see <see cref="Shared.Cnpj"/>): 14 characters.</summary>
    Cnpj,
}

/// <summary>
/// A customer's document: the CPF of a person or the CNPJ of a company. Kept without punctuation; it is also the
/// customer's login, and the two never collide (11 digits against 14 characters).
/// </summary>
public sealed record TaxId
{
    private const int CpfLength = 11;

    public string Value { get; }
    public TaxIdKind Kind { get; }

    private TaxId(string value, TaxIdKind kind) => (Value, Kind) = (value, kind);

    public static bool TryParse(string? input, out TaxId? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var digits = new string(input.Where(c => c is not ('.' or '/' or '-') && !char.IsWhiteSpace(c)).ToArray());
        if (digits.Length == CpfLength)
        {
            if (!IsValidCpf(digits))
                return false;
            document = new TaxId(digits, TaxIdKind.Cpf);
            return true;
        }

        if (!Shared.Cnpj.TryParse(input, out var cnpj))
            return false;
        document = new TaxId(cnpj!.Value, TaxIdKind.Cnpj);
        return true;
    }

    public static TaxId Parse(string input) =>
        TryParse(input, out var document) ? document! : throw new FormatException($"'{input}' is not a valid CPF or CNPJ.");

    public override string ToString() => Value;

    private static bool IsValidCpf(string digits)
    {
        if (!digits.All(char.IsAsciiDigit) || digits.Distinct().Count() == 1)
            return false;

        return CheckDigit(digits[..9]) == digits[9] - '0' && CheckDigit(digits[..10]) == digits[10] - '0';
    }

    // Weights go down from length + 1 to 2: 10..2 for the first digit, 11..2 for the second.
    private static int CheckDigit(string body)
    {
        var sum = body.Select((c, i) => (c - '0') * (body.Length + 1 - i)).Sum();
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
