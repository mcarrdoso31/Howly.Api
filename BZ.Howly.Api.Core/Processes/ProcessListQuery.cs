namespace BZ.Howly.Api.Core.Processes;

/// <summary>
/// Filters of the process lists. Every filter is optional; <see cref="Overdue"/> keeps only late active steps and
/// <see cref="RenewalDue"/> the licenses whose renewal should have started and was not opened yet.
/// </summary>
public sealed record ProcessListQuery(
    string? Search = null,
    Guid? CustomerId = null,
    ProcessStatus? Status = null,
    StepOwner? WaitingOn = null,
    Guid? AssigneeId = null,
    bool Overdue = false,
    Guid? ParentId = null,
    int Page = 1,
    int PageSize = 20,
    bool RenewalDue = false);
