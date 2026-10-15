using BZ.Howly.Api.Core.DTO.Response;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>The first-access checklist of the current consultancy.</summary>
public interface IOnboardingService
{
    Task<OnboardingResponse> GetAsync(CancellationToken cancellationToken = default);
}
