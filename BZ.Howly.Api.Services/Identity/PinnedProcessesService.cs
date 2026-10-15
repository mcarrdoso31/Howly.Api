using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Services.Identity;

public sealed class PinnedProcessesService(
    IUserPreferencesRepository preferencesRepository, IProcessRepository processRepository, TimeProvider timeProvider) : IPinnedProcessesService
{
    public async Task<PinnedProcessesResponse> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var pinned = (await preferencesRepository.FindAsync(userId, cancellationToken))?.PinnedProcessIds ?? [];
        var existing = await processRepository.ListExistingIdsAsync([.. pinned], cancellationToken);
        return new PinnedProcessesResponse([.. pinned.Where(existing.Contains)]);
    }

    public async Task<OperationResult<PinnedProcessesResponse>> SaveAsync(
        Guid userId, PinnedProcessesRequest request, CancellationToken cancellationToken = default)
    {
        var errors = UserPreferences.ValidatePins(request.ProcessIds);
        if (errors.Count > 0)
            return OperationResult<PinnedProcessesResponse>.Invalid(errors);

        var ids = request.ProcessIds!;
        var existing = await processRepository.ListExistingIdsAsync([.. ids], cancellationToken);
        if (existing.Count != ids.Count)
            return OperationResult<PinnedProcessesResponse>.Invalid("processIds", "Pin only processes of this consultancy.");

        var preferences = await preferencesRepository.FindAsync(userId, cancellationToken);
        if (preferences is null)
            await preferencesRepository.AddAsync(UserPreferences.CreateWithPins(userId, ids, timeProvider.GetUtcNow()), cancellationToken);
        else
            preferences.Pin(ids);

        await preferencesRepository.SaveChangesAsync(cancellationToken);
        return OperationResult<PinnedProcessesResponse>.Success(new PinnedProcessesResponse([.. ids]));
    }
}
