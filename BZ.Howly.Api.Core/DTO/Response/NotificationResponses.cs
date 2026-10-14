using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.DTO.Response;

/// <param name="Details">The document name or the new assignee name, depending on the type.</param>
public sealed record NotificationResponse(
    Guid Id,
    Guid ProcessId,
    string ProcessTitle,
    ProcessEventType Type,
    string? StepName,
    string? Details,
    string ActorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationPageResponse(
    IReadOnlyList<NotificationResponse> Items, int UnreadCount, int Page, int PageSize, int TotalCount);
