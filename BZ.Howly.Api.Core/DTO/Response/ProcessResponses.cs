using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.DTO.Response;

/// <param name="WaitingOn">Owner of the active step while in progress; null otherwise.</param>
/// <param name="ActiveStepAgencyName">Which agency, when the active step is with one.</param>
public sealed record ProcessListItemResponse(
    Guid Id,
    string Title,
    Guid CustomerId,
    string CustomerName,
    string ProcessTypeName,
    ProcessStatus Status,
    StepOwner? WaitingOn,
    string? ActiveStepName,
    string? ActiveStepAgencyName,
    DateTimeOffset? ActiveStepStartedAt,
    DateTimeOffset? ActiveStepDueAt,
    Guid AssigneeId,
    string AssigneeName,
    string? AssigneeAvatarVersion,
    int StepsDone,
    int StepsTotal,
    Guid? ParentId,
    DateTimeOffset UpdatedAt,
    int DocumentsToSend);

public sealed record PersonResponse(Guid Id, string Name, string? AvatarVersion);

public sealed record ProcessParentResponse(Guid Id, string Title);

public sealed record ProcessResponse(
    Guid Id,
    string Title,
    PersonResponse Customer,
    Guid ProcessTypeId,
    string ProcessTypeName,
    ProcessStatus Status,
    string? StatusReason,
    PersonResponse Assignee,
    ProcessParentResponse? Parent,
    StepOwner? WaitingOn,
    DateTimeOffset? ActiveStepDueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ProcessStepResponse> Steps,
    IReadOnlyList<ProcessEventResponse> Events,
    IReadOnlyList<ProcessListItemResponse> Children,
    IReadOnlyList<ProcessDocumentResponse> Documents);

/// <param name="StepId">Null for extra documents, which belong to the whole process.</param>
/// <param name="MayContainMacros">Office format that can carry macros: the screens warn before opening it.</param>
public sealed record ProcessDocumentResponse(
    Guid Id,
    Guid? StepId,
    DocumentKind Kind,
    string Name,
    bool IsRequired,
    DocumentStatus Status,
    bool MayContainMacros,
    IReadOnlyList<DocumentVersionResponse> Versions)
{
    public static ProcessDocumentResponse From(ProcessDocument d) => new(
        d.Id, d.StepId, d.Kind, d.Name, d.IsRequired, d.Status,
        d.LatestVersion is { } latest && Shared.UploadPolicy.MayContainMacros(latest.FileName),
        d.Versions.Select(v => new DocumentVersionResponse(
            v.Id, v.Number, v.FileName, v.Size, v.UploadedByName, v.UploadedAt, v.Status, v.ReviewReason, v.ReviewedByName, v.ReviewedAt)).ToList());
}

public sealed record DocumentVersionResponse(
    Guid Id,
    int Number,
    string FileName,
    long Size,
    string UploadedByName,
    DateTimeOffset UploadedAt,
    DocumentStatus Status,
    string? ReviewReason,
    string? ReviewedByName,
    DateTimeOffset? ReviewedAt);

public sealed record ProcessStepResponse(
    Guid Id,
    string Name,
    string? Description,
    StepOwner Owner,
    string? AgencyName,
    int? DurationDays,
    bool IsOptional,
    string? CustomerChecklist,
    string? DeliveryChecklist,
    StepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? DueAt,
    Guid? AgencyId)
{
    public static ProcessStepResponse From(ProcessStep s) => new(
        s.Id, s.Name, s.Description, s.Owner, s.AgencyName, s.DurationDays, s.IsOptional, s.CustomerChecklist, s.DeliveryChecklist,
        s.Status, s.StartedAt, s.FinishedAt, s.DueAt, s.AgencyId);
}

/// <param name="SeenByCustomerAt">When the customer read the notification of this event; null if unread or never notified.</param>
public sealed record ProcessEventResponse(
    Guid Id, ProcessEventType Type, string? StepName, string? Comment, string? Details, string ActorName, DateTimeOffset At,
    DateTimeOffset? SeenByCustomerAt, bool ByCustomer)
{
    public static ProcessEventResponse From(ProcessEvent e, IReadOnlyDictionary<Guid, DateTimeOffset> seenByCustomer, Guid? customerUserId) =>
        new(e.Id, e.Type, e.StepName, e.Comment, e.Details, e.ActorName, e.At, seenByCustomer.TryGetValue(e.Id, out var seen) ? seen : null,
            e.ActorId == customerUserId);
}

/// <summary>The staff home page counts: open processes, the late ones and who each one waits on.</summary>
public sealed record ProcessSummaryResponse(
    int Open, int Overdue, int WaitingOnCustomer, int WaitingOnStaff, int WaitingOnAgency, int WaitingOnPayment);
