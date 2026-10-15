using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Services.Identity;

public sealed class PreferencesService(IUserPreferencesRepository repository, TimeProvider timeProvider) : IPreferencesService
{
    public async Task<PreferencesResponse?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var preferences = await repository.FindAsync(userId, cancellationToken);
        return preferences is { HasAppearance: true } ? ToResponse(preferences) : null;
    }

    public async Task<OperationResult<PreferencesResponse>> SaveAsync(
        Guid userId, UpdatePreferencesRequest request, CancellationToken cancellationToken = default)
    {
        var errors = UserPreferences.Validate(request.ThemeMode, request.ThemePreset, request.PrimaryColor, request.Language);
        if (errors.Count > 0)
            return OperationResult<PreferencesResponse>.Invalid(errors);

        var now = timeProvider.GetUtcNow();
        var preferences = await repository.FindAsync(userId, cancellationToken);
        if (preferences is null)
        {
            preferences = UserPreferences.Create(userId, request.ThemeMode, request.ThemePreset, request.PrimaryColor, request.Language, now);
            await repository.AddAsync(preferences, cancellationToken);
        }
        else
        {
            preferences.Update(request.ThemeMode, request.ThemePreset, request.PrimaryColor, request.Language, now);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return OperationResult<PreferencesResponse>.Success(ToResponse(preferences));
    }

    private static PreferencesResponse ToResponse(UserPreferences p) =>
        new(p.ThemeMode, p.ThemePreset, p.PrimaryColor, p.Language, p.UpdatedAt);
}
