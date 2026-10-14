using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>The agencies catalog of the consultancy (analysts and admins keep it).</summary>
public interface IAgencyService
{
    Task<IReadOnlyList<AgencyResponse>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<OperationResult<AgencyResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OperationResult<AgencyResponse>> CreateAsync(AgencyRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<AgencyResponse>> UpdateAsync(Guid id, AgencyRequest request, CancellationToken cancellationToken = default);
    Task<OperationResult<AgencyResponse>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
}
