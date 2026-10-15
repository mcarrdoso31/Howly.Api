using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Tenancy;

namespace BZ.Howly.Api.Services.Tenancy;

public sealed class OnboardingService(IOnboardingRepository repository, ITenantContext tenantContext) : IOnboardingService
{
    public Task<OnboardingResponse> GetAsync(CancellationToken cancellationToken = default) =>
        repository.GetAsync(tenantContext.TenantId ?? throw new InvalidOperationException("No consultancy in this request."), cancellationToken);
}
