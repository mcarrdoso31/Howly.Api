using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Processes;

/// <summary>Step data as typed by the staff, before validation.</summary>
/// <param name="Documents">Files the customer must send in this step, each one a field of its own.</param>
/// <param name="Deliverables">Files the company delivers in this step (protocol, payment slip, license…).</param>
/// <param name="AgencyName">Agency steps: how the agency shows (the acronym of <paramref name="AgencyId"/> when one is chosen).</param>
/// <param name="AgencyId">Agency steps: the default agency, from the catalog; each process can change it.</param>
/// <param name="Notes">Anything else about the step, in the staff's words.</param>
/// <param name="Id">An existing step of the type being edited: it keeps its identity.</param>
public sealed record StepTemplateDetails(
    string? Name,
    string? Description,
    StepOwner? Owner,
    string? AgencyName,
    int? DurationDays,
    bool IsOptional,
    string? Notes,
    IReadOnlyList<DocumentRequirement>? Documents,
    IReadOnlyList<string>? Deliverables,
    Guid? AgencyId = null,
    Guid? Id = null);

/// <summary>A document a step asks for (e.g. "Licença de operação de máquinas agrícolas").</summary>
/// <param name="ExampleId">A <see cref="DocumentExample"/> for the customer to download and follow.</param>
/// <param name="ExampleFileName">The example's file name, set by the server from <paramref name="ExampleId"/>.</param>
public sealed record DocumentRequirement(string? Name, bool IsRequired, Guid? ExampleId = null, string? ExampleFileName = null);

/// <summary>A step of a <see cref="ProcessType"/>. Processes copy these when they are opened.</summary>
public sealed class StepTemplate
{
    public Guid Id { get; private set; }

    /// <summary>Order inside the type (the database does not keep insertion order).</summary>
    public int Position { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public StepOwner Owner { get; private set; }

    /// <summary>Only for <see cref="StepOwner.Agency"/> steps: how the default agency shows (IBAMA, CETESB…).</summary>
    public string? AgencyName { get; private set; }

    /// <summary>Only for <see cref="StepOwner.Agency"/> steps: the default agency of the processes opened from this type.</summary>
    public Guid? AgencyId { get; private set; }

    /// <summary>Expected duration; a step taking longer is shown as overdue. Null means no deadline.</summary>
    public int? DurationDays { get; private set; }

    /// <summary>Optional steps can be skipped.</summary>
    public bool IsOptional { get; private set; }

    /// <summary>Anything else about the step, in the staff's words.</summary>
    public string? Notes { get; private set; }

    /// <summary>Documents the customer sends in this step; each becomes a field of the process when it is opened.</summary>
    public IReadOnlyList<DocumentRequirement> Documents { get; private set; } = [];

    /// <summary>Files the company delivers in this step; each becomes a slot of the process when it is opened.</summary>
    public IReadOnlyList<string> Deliverables { get; private set; } = [];

    private StepTemplate() { }

    /// <summary>Builds a step from data already checked by <see cref="ProcessType.Validate"/>.</summary>
    internal static StepTemplate FromValidated(StepTemplateDetails details, int position)
    {
        var step = new StepTemplate { Id = Ids.New() };
        step.Apply(details, position);
        return step;
    }

    /// <summary>Replaces everything but the identity, from data already checked by <see cref="ProcessType.Validate"/>.</summary>
    internal void Apply(StepTemplateDetails details, int position)
    {
        Position = position;
        Name = details.Name!.Trim();
        Description = ProcessText.Blank(details.Description);
        Owner = details.Owner!.Value;
        AgencyName = details.Owner == StepOwner.Agency ? ProcessText.Blank(details.AgencyName) : null;
        AgencyId = details.Owner == StepOwner.Agency ? details.AgencyId : null;
        DurationDays = details.DurationDays;
        IsOptional = details.IsOptional;
        Notes = ProcessText.Blank(details.Notes);
        Documents = (details.Documents ?? []).Select(d => new DocumentRequirement(d.Name!.Trim(), d.IsRequired, d.ExampleId, d.ExampleId is null ? null : d.ExampleFileName)).ToList();
        Deliverables = (details.Deliverables ?? []).Select(d => d.Trim()).ToList();
    }
}

internal static class ProcessText
{
    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
