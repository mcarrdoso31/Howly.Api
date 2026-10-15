using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.DTO.Request;

/// <summary>Create or replace a process type; the step list replaces the current one, in the given order.</summary>
public sealed record ProcessTypeRequest(string? Name, string? Description, IReadOnlyList<StepTemplateRequest>? Steps)
{
    /// <summary>A null step in the body becomes an empty one, so validation points to its fields instead of failing.</summary>
    public IReadOnlyList<StepTemplateDetails> StepDetails() =>
        Steps?.Select(s => s is null
            ? new StepTemplateDetails(null, null, null, null, null, false, null, null, null)
            // The agency name comes from the catalog (set by the service), never typed.
            : new StepTemplateDetails(
                s.Name, s.Description, s.Owner, null, s.DurationDays, s.IsOptional, s.Notes, s.Documents, s.Deliverables, s.AgencyId, s.Id)).ToList() ?? [];
}

/// <param name="Owner">Required: a missing owner is an error, never a silent "Customer".</param>
/// <param name="AgencyId">Agency steps: the agency of the catalog (optional: each process can set it); its acronym names it.</param>
/// <param name="Id">Editing a type: the id of the step this one is (it keeps its identity); null for a new step.</param>
public sealed record StepTemplateRequest(
    string? Name,
    string? Description,
    StepOwner? Owner,
    int? DurationDays,
    bool IsOptional,
    string? Notes,
    IReadOnlyList<DocumentRequirement>? Documents,
    IReadOnlyList<string>? Deliverables,
    Guid? AgencyId = null,
    Guid? Id = null);
