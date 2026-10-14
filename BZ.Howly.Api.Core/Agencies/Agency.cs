using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Agencies;

public enum AgencySphere
{
    Federal,
    State,
    Municipal,
}

/// <summary>Agency data as typed by the staff, before validation.</summary>
/// <param name="State">Two-letter code; required for state and municipal agencies.</param>
/// <param name="City">Required for municipal agencies.</param>
/// <param name="ProtocolSystem">Where requests are filed (SEI, e-ambiente…), free text.</param>
public sealed record AgencyDetails(
    string? Acronym, string? Name, AgencySphere? Sphere, string? State, string? City, string? Website, string? ProtocolSystem);

/// <summary>
/// A public body the consultancy deals with (IBAMA, CETESB, a city's environment office…). Agency steps of the process
/// types and processes point to one. Deactivated instead of deleted: processes keep referring to it.
/// </summary>
public sealed class Agency
{
    public const int MaxAcronymLength = 20;
    public const int MaxNameLength = 200;
    public const int MaxCityLength = 100;
    public const int MaxWebsiteLength = 300;
    public const int MaxProtocolSystemLength = 100;

    public Guid Id { get; private set; }
    public string Acronym { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AgencySphere Sphere { get; private set; }
    public string? State { get; private set; }
    public string? City { get; private set; }
    public string? Website { get; private set; }
    public string? ProtocolSystem { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>What steps and lists show: the acronym, which is how people call agencies.</summary>
    public string DisplayName => Acronym;

    private Agency() { }

    /// <summary>Field errors keyed like the request body.</summary>
    public static IReadOnlyDictionary<string, string[]> Validate(AgencyDetails details)
    {
        var errors = new Dictionary<string, string[]>();
        var acronym = Text(details.Acronym);
        if (acronym is null || acronym.Length > MaxAcronymLength)
            errors["acronym"] = [$"The acronym is required and must have at most {MaxAcronymLength} characters."];
        var name = Text(details.Name);
        if (name is null || name.Length > MaxNameLength)
            errors["name"] = [$"The name is required and must have at most {MaxNameLength} characters."];
        if (details.Sphere is not { } sphere || !Enum.IsDefined(sphere))
            errors["sphere"] = ["Sphere must be Federal, State or Municipal."];

        if (details.Sphere is AgencySphere.State or AgencySphere.Municipal && !BrazilianStates.IsValid(Text(details.State)))
            errors["state"] = ["State and municipal agencies need their state (SP, MG…)."];
        if (details.Sphere == AgencySphere.Municipal && (Text(details.City) is not { } city || city.Length > MaxCityLength))
            errors["city"] = [$"Municipal agencies need their city, with at most {MaxCityLength} characters."];
        if (Text(details.Website)?.Length > MaxWebsiteLength)
            errors["website"] = [$"The website must have at most {MaxWebsiteLength} characters."];
        if (Text(details.ProtocolSystem)?.Length > MaxProtocolSystemLength)
            errors["protocolSystem"] = [$"The protocol system must have at most {MaxProtocolSystemLength} characters."];
        return errors;
    }

    public static Agency Create(AgencyDetails details, DateTimeOffset now)
    {
        var agency = new Agency { Id = Ids.New(), IsActive = true, CreatedAt = now };
        agency.Update(details, now);
        return agency;
    }

    /// <summary>Replaces the data; state and city only stay where the sphere uses them.</summary>
    public void Update(AgencyDetails details, DateTimeOffset now)
    {
        var errors = Validate(details);
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid agency: {string.Join(", ", errors.Keys)}.");

        Acronym = Text(details.Acronym)!;
        Name = Text(details.Name)!;
        Sphere = details.Sphere!.Value;
        State = Sphere == AgencySphere.Federal ? null : Text(details.State)!.ToUpperInvariant();
        City = Sphere == AgencySphere.Municipal ? Text(details.City) : null;
        Website = Text(details.Website);
        ProtocolSystem = Text(details.ProtocolSystem);
        UpdatedAt = now;
    }

    /// <summary>Hidden from new choices; process types and processes that use it keep it.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
