using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>The first-access checklist of the staff home page: which first steps the consultancy already took.</summary>
[ApiController]
[Route("api/onboarding")]
[Authorize(Policy = Policies.Internal)]
public sealed class OnboardingController(IOnboardingService onboardingService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<OnboardingResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => Ok(await onboardingService.GetAsync(cancellationToken));
}
