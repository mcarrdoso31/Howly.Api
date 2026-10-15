using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IProcessRepository
{
    Task AddAsync(Process process, CancellationToken cancellationToken = default);

    /// <summary>With steps and timeline.</summary>
    Task<Process?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The customer of the process, without loading it; null when it is not a process of the current consultancy.</summary>
    Task<Guid?> FindCustomerIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of the timeline, newest first: up to <paramref name="take"/> events before the one numbered
    /// <paramref name="beforeSequence"/> (from the newest when null), only those the customer sees when asked.
    /// </summary>
    Task<IReadOnlyList<ProcessEvent>> ListEventsAsync(
        Guid processId, int? beforeSequence, int take, bool visibleToCustomerOnly, CancellationToken cancellationToken = default);

    /// <summary>Which of <paramref name="ids"/> are processes of the current consultancy.</summary>
    Task<IReadOnlyList<Guid>> ListExistingIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Most recently changed first. <paramref name="now"/> decides what is overdue; <paramref name="today"/> (local date)
    /// which renewals are due.
    /// </summary>
    Task<(IReadOnlyList<ProcessListItemResponse> Items, int TotalCount)> SearchAsync(
        ProcessListQuery query, DateTimeOffset now, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>In-progress or paused processes under <paramref name="assigneeId"/>, ready to be changed.</summary>
    Task<IReadOnlyList<Process>> ListOpenAssignedToAsync(Guid assigneeId, CancellationToken cancellationToken = default);

    /// <summary>How many in-progress or paused processes are under <paramref name="assigneeId"/>, without loading them.</summary>
    Task<int> CountOpenAssignedToAsync(Guid assigneeId, CancellationToken cancellationToken = default);

    /// <summary>The home page counts (of <paramref name="assigneeId"/>'s processes when given), in one query.</summary>
    Task<ProcessSummaryResponse> SummarizeAsync(Guid? assigneeId, DateTimeOffset now, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Paused processes whose expected return arrived (on <paramref name="today"/>, local date) and were not reminded yet.</summary>
    Task<IReadOnlyList<Process>> ListPausedDueAsync(DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Processes whose license renewal should start by <paramref name="today"/> (local date), not reminded nor opened yet.</summary>
    Task<IReadOnlyList<Process>> ListRenewalsDueAsync(DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Subprocesses of <paramref name="parentId"/> still in progress or paused.</summary>
    Task<int> CountOpenSubprocessesAsync(Guid parentId, CancellationToken cancellationToken = default);

    /// <summary>In-progress processes whose active step deadline is before <paramref name="now"/>, ready to be changed.</summary>
    Task<IReadOnlyList<Process>> ListOverdueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
