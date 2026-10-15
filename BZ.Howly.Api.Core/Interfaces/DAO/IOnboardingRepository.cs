using BZ.Howly.Api.Core.DTO.Response;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IOnboardingRepository
{
    /// <summary>What the consultancy <paramref name="tenantId"/> already has, in one round trip per step.</summary>
    Task<OnboardingResponse> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
