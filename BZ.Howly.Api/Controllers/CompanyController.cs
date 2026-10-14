using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>The signed-in customer's company: reviewed and confirmed on first access, then kept up to date by the customer.</summary>
[ApiController]
[Route("api/me/company")]
[Authorize(Policy = Policies.Customer)]
[AllowPendingProfileConfirmation]
public sealed class CompanyController(ICustomerProfileService profileService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CustomerProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.ToActionResult(await profileService.GetAsync(this.CurrentUserId(), cancellationToken), Ok);

    /// <summary>After this call the client refreshes the session: the new token no longer asks for confirmation.</summary>
    [HttpPost("confirm")]
    [ProducesResponseType<CustomerProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Confirm(ContactsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await profileService.ConfirmAsync(this.CurrentUserId(), request, cancellationToken), Ok);

    [HttpPut("contacts")]
    [ProducesResponseType<CustomerProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateContacts(ContactsRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await profileService.UpdateContactsAsync(this.CurrentUserId(), request, cancellationToken), Ok);

    [HttpPut("trade-name")]
    [ProducesResponseType<CustomerProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateTradeName(UpdateTradeNameRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await profileService.UpdateTradeNameAsync(this.CurrentUserId(), request, cancellationToken), Ok);
}
