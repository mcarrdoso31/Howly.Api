using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>The processes a staff member keeps on their home page. Each person sees only their own.</summary>
public interface IPinnedProcessesService
{
    /// <summary>In the saved order, without the processes that no longer exist in the consultancy.</summary>
    Task<PinnedProcessesResponse> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<OperationResult<PinnedProcessesResponse>> SaveAsync(Guid userId, PinnedProcessesRequest request, CancellationToken cancellationToken = default);
}
