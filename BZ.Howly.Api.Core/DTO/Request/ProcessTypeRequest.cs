using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.DTO.Request;

/// <summary>Create or replace a process type; the step list replaces the current one, in the given order.</summary>
public sealed record ProcessTypeRequest(string? Name, string? Description, IReadOnlyList<StepTemplateRequest>? Steps)
{
    /// <summary>A null step in the body becomes an empty one, so validation points to its fields instead of failing.</summary>
    public IReadOnlyList<StepTemplateDetails> StepDetails() =>
        Steps?.Select(s => s is null
            ? new StepTemplateDetails(null, null, null, null, null, false, null, null, null, null)
            : new StepTemplateDetails(
                s.Name, s.Description, s.Owner, s.AgencyName, s.DurationDays, s.IsOptional, s.CustomerChecklist, s.DeliveryChecklist,
                s.Documents, s.Deliverables, s.AgencyId)).ToList() ?? [];
}

/// <param name="Owner">Required: a missing owner is an error, never a silent "Customer".</param>
/// <param name="AgencyId">Agency steps: the default agency (from the catalog); its acronym becomes the agency name.</param>
public sealed record StepTemplateRequest(
    string? Name,
    string? Description,
    StepOwner? Owner,
    string? AgencyName,
    int? DurationDays,
    bool IsOptional,
    string? CustomerChecklist,
    string? DeliveryChecklist,
    IReadOnlyList<DocumentRequirement>? Documents,
    IReadOnlyList<string>? Deliverables,
    Guid? AgencyId = null);
