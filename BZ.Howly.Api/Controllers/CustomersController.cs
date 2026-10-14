using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = Policies.Internal)]
public sealed class CustomersController(ICustomerService customerService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CustomerCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateCustomerRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(
            await customerService.CreateAsync(request, this.CurrentUserId(), cancellationToken),
            created => CreatedAtAction(nameof(Get), new { id = created.Id }, created));

    [HttpGet]
    [ProducesResponseType<PagedResponse<CustomerListItemResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] CustomerStatusFilter status = CustomerStatusFilter.Active,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await customerService.ListAsync(search, status, page, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await customerService.GetAsync(id, cancellationToken), Ok);

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await customerService.UpdateAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await customerService.DeactivateAsync(id, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await customerService.ReactivateAsync(id, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType<TemporaryPasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await customerService.ResetPasswordAsync(id, this.CurrentUserId(), cancellationToken), Ok);
}
