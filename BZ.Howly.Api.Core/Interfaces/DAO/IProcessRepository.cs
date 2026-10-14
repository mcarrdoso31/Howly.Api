using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IProcessRepository
{
    Task AddAsync(Process process, CancellationToken cancellationToken = default);

    /// <summary>With steps and timeline.</summary>
    Task<Process?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Most recently changed first. <paramref name="now"/> decides what is overdue.</summary>
    Task<(IReadOnlyList<ProcessListItemResponse> Items, int TotalCount)> SearchAsync(
        ProcessListQuery query, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>In-progress or paused processes under <paramref name="assigneeId"/>, ready to be changed.</summary>
    Task<IReadOnlyList<Process>> ListOpenAssignedToAsync(Guid assigneeId, CancellationToken cancellationToken = default);

    /// <summary>Subprocesses of <paramref name="parentId"/> still in progress or paused.</summary>
    Task<int> CountOpenSubprocessesAsync(Guid parentId, CancellationToken cancellationToken = default);

    /// <summary>In-progress processes whose active step deadline is before <paramref name="now"/>, ready to be changed.</summary>
    Task<IReadOnlyList<Process>> ListOverdueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
