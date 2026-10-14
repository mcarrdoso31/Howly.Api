using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.DTO.Response;

public sealed record ProcessTypeListItemResponse(Guid Id, string Name, string? Description, bool IsActive, int StepCount);

public sealed record ProcessTypeResponse(
    Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<StepTemplateResponse> Steps,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static ProcessTypeResponse From(ProcessType type) => new(
        type.Id, type.Name, type.Description, type.IsActive,
        type.Steps.Select(s => new StepTemplateResponse(
            s.Id, s.Name, s.Description, s.Owner, s.AgencyName, s.DurationDays, s.IsOptional, s.CustomerChecklist, s.DeliveryChecklist,
            s.Documents, s.Deliverables, s.AgencyId)).ToList(),
        type.CreatedAt, type.UpdatedAt);
}

public sealed record StepTemplateResponse(
    Guid Id,
    string Name,
    string? Description,
    StepOwner Owner,
    string? AgencyName,
    int? DurationDays,
    bool IsOptional,
    string? CustomerChecklist,
    string? DeliveryChecklist,
    IReadOnlyList<DocumentRequirement> Documents,
    IReadOnlyList<string> Deliverables,
    Guid? AgencyId);
