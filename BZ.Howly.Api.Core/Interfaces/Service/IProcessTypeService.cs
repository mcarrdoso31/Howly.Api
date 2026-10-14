using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

public interface IProcessTypeService
{
    Task<IReadOnlyList<ProcessTypeListItemResponse>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessTypeResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessTypeResponse>> CreateAsync(ProcessTypeRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessTypeResponse>> UpdateAsync(Guid id, ProcessTypeRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessTypeResponse>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
}
