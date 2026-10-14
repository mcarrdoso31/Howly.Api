namespace BZ.Howly.Api.Core.Shared;

/// <summary>The two-letter codes of the Brazilian states (UF).</summary>
public static class BrazilianStates
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ",
        "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
    };

    public static bool IsValid(string? code) => code is not null && All.Contains(code.Trim());
}
