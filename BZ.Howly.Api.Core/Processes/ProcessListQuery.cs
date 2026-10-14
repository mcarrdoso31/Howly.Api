namespace BZ.Howly.Api.Core.Processes;

/// <summary>Filters of the process lists. Every filter is optional; <see cref="Overdue"/> keeps only late active steps.</summary>
public sealed record ProcessListQuery(
    string? Search = null,
    Guid? CustomerId = null,
    ProcessStatus? Status = null,
    StepOwner? WaitingOn = null,
    Guid? AssigneeId = null,
    bool Overdue = false,
    Guid? ParentId = null,
    int Page = 1,
    int PageSize = 20);
