using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

public interface IPreferencesService
{
    /// <summary>Null while the user never saved preferences, so the client keeps its local choices.</summary>
    Task<PreferencesResponse?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<OperationResult<PreferencesResponse>> SaveAsync(Guid userId, UpdatePreferencesRequest request, CancellationToken cancellationToken = default);
}
