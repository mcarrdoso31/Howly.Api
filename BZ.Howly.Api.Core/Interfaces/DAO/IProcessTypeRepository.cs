using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IProcessTypeRepository
{
    Task AddAsync(ProcessType type, CancellationToken cancellationToken = default);
    Task<ProcessType?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Ordered by name.</summary>
    Task<IReadOnlyList<ProcessTypeListItemResponse>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
