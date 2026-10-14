using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Processes;

/// <summary>Step data as typed by the staff, before validation.</summary>
/// <param name="Documents">Files the customer must send in this step, each one a field of its own.</param>
/// <param name="Deliverables">Files the company delivers in this step (protocol, payment slip, license…).</param>
public sealed record StepTemplateDetails(
    string? Name,
    string? Description,
    StepOwner? Owner,
    string? AgencyName,
    int? DurationDays,
    bool IsOptional,
    string? CustomerChecklist,
    string? DeliveryChecklist,
    IReadOnlyList<DocumentRequirement>? Documents,
    IReadOnlyList<string>? Deliverables);

/// <summary>A document a step asks for (e.g. "Licença de operação de máquinas agrícolas").</summary>
public sealed record DocumentRequirement(string? Name, bool IsRequired);

/// <summary>A step of a <see cref="ProcessType"/>. Processes copy these when they are opened.</summary>
public sealed class StepTemplate
{
    public Guid Id { get; private set; }

    /// <summary>Order inside the type (the database does not keep insertion order).</summary>
    public int Position { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public StepOwner Owner { get; private set; }

    /// <summary>Only for <see cref="StepOwner.Agency"/> steps: which agency (IBAMA, CETESB…).</summary>
    public string? AgencyName { get; private set; }

    /// <summary>Expected duration; a step taking longer is shown as overdue. Null means no deadline.</summary>
    public int? DurationDays { get; private set; }

    /// <summary>Optional steps can be skipped.</summary>
    public bool IsOptional { get; private set; }

    /// <summary>What the customer must send in this step (free text until documents arrive in P2).</summary>
    public string? CustomerChecklist { get; private set; }

    /// <summary>What the company delivers in this step (protocol, payment slip, license…).</summary>
    public string? DeliveryChecklist { get; private set; }

    /// <summary>Documents the customer sends in this step; each becomes a field of the process when it is opened.</summary>
    public IReadOnlyList<DocumentRequirement> Documents { get; private set; } = [];

    /// <summary>Files the company delivers in this step; each becomes a slot of the process when it is opened.</summary>
    public IReadOnlyList<string> Deliverables { get; private set; } = [];

    private StepTemplate() { }

    /// <summary>Builds a step from data already checked by <see cref="ProcessType.Validate"/>.</summary>
    internal static StepTemplate FromValidated(StepTemplateDetails details, int position) => new()
    {
        Id = Ids.New(),
        Position = position,
        Name = details.Name!.Trim(),
        Description = ProcessText.Blank(details.Description),
        Owner = details.Owner!.Value,
        AgencyName = details.Owner == StepOwner.Agency ? ProcessText.Blank(details.AgencyName) : null,
        DurationDays = details.DurationDays,
        IsOptional = details.IsOptional,
        CustomerChecklist = ProcessText.Blank(details.CustomerChecklist),
        DeliveryChecklist = ProcessText.Blank(details.DeliveryChecklist),
        Documents = (details.Documents ?? []).Select(d => new DocumentRequirement(d.Name!.Trim(), d.IsRequired)).ToList(),
        Deliverables = (details.Deliverables ?? []).Select(d => d.Trim()).ToList(),
    };
}

internal static class ProcessText
{
    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
