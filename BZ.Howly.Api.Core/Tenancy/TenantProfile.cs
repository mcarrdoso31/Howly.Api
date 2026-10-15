using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Tenancy;

/// <summary>
/// The consultancy's own data, all optional except the time zone. Stored normalized: digits only for CNPJ, phone and
/// postal code; the state as its two-letter code.
/// </summary>
/// <param name="TimeZone">IANA id (e.g. "America/Sao_Paulo"): the local day deadlines are counted in.</param>
public sealed record TenantProfile(
    string? LegalName,
    string? Cnpj,
    string? Email,
    string? Phone,
    string? PostalCode,
    string? Street,
    string? City,
    string? State,
    string TimeZone)
{
    public const string DefaultTimeZone = "America/Sao_Paulo";
    public const int MaxLegalNameLength = 200;
    public const int MaxEmailLength = 256;
    public const int MaxStreetLength = 200;
    public const int MaxCityLength = 100;

    public static readonly TenantProfile Empty = new(null, null, null, null, null, null, null, null, DefaultTimeZone);

    /// <summary>The consultancy's calendar day at <paramref name="now"/> (deadlines and expected returns are local days).</summary>
    public DateOnly LocalDate(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out var zone) ? TimeZoneInfo.ConvertTime(now, zone).DateTime : now.UtcDateTime);

    /// <summary>
    /// Checks typed data and returns it normalized, or the errors keyed like the request body ("name", "cnpj"…).
    /// </summary>
    public static (string? Name, TenantProfile? Profile, IReadOnlyDictionary<string, string[]> Errors) Normalize(
        string? name, string? legalName, string? cnpj, string? email, string? phone, string? postalCode,
        string? street, string? city, string? state, string? timeZone)
    {
        var errors = new Dictionary<string, string[]>();
        string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var cleanName = Text(name);
        if (cleanName is null || cleanName.Length > Tenant.MaxNameLength)
            errors["name"] = [$"The name is required and must have at most {Tenant.MaxNameLength} characters."];
        if (Text(legalName)?.Length > MaxLegalNameLength)
            errors["legalName"] = [$"The legal name must have at most {MaxLegalNameLength} characters."];

        string? cleanCnpj = null;
        if (Text(cnpj) is { } typedCnpj)
        {
            if (Shared.Cnpj.TryParse(typedCnpj, out var parsed)) cleanCnpj = parsed!.Value;
            else errors["cnpj"] = ["CNPJ is invalid."];
        }

        if (Text(email) is { } typedEmail && (typedEmail.Length > MaxEmailLength || !EmailAddress.IsValid(typedEmail)))
            errors["email"] = ["E-mail is invalid."];

        string? cleanPhone = null;
        if (Text(phone) is { } typedPhone)
        {
            if (BrazilianPhone.TryNormalize(typedPhone, out var digits)) cleanPhone = digits;
            else errors["phone"] = ["Phone is invalid."];
        }

        string? cleanPostalCode = null;
        if (Text(postalCode) is { } typedPostalCode)
        {
            var digits = new string(typedPostalCode.Where(char.IsDigit).ToArray());
            if (digits.Length == 8 && typedPostalCode.All(c => char.IsDigit(c) || c is '-' or '.' or ' ')) cleanPostalCode = digits;
            else errors["postalCode"] = ["CEP must have 8 digits."];
        }

        if (Text(street)?.Length > MaxStreetLength)
            errors["street"] = [$"The street must have at most {MaxStreetLength} characters."];
        if (Text(city)?.Length > MaxCityLength)
            errors["city"] = [$"The city must have at most {MaxCityLength} characters."];

        var cleanState = Text(state)?.ToUpperInvariant();
        if (cleanState is not null && !BrazilianStates.IsValid(cleanState))
            errors["state"] = ["State must be a Brazilian state code (SP, MG…)."];

        var cleanTimeZone = Text(timeZone) ?? DefaultTimeZone;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(cleanTimeZone, out _))
            errors["timeZone"] = ["Unknown time zone."];

        if (errors.Count > 0)
            return (null, null, errors);

        var profile = new TenantProfile(Text(legalName), cleanCnpj, Text(email), cleanPhone, cleanPostalCode, Text(street), Text(city), cleanState, cleanTimeZone);
        return (cleanName, profile, errors);
    }
}
