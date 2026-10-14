using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

[ApiController]
[Route("api/internal-users")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class InternalUsersController(IInternalUserService internalUserService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<InternalUserCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateInternalUserRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(
            await internalUserService.CreateAsync(request, cancellationToken),
            created => CreatedAtAction(nameof(Get), new { id = created.Id }, created));

    [HttpGet]
    [ProducesResponseType<PagedResponse<InternalUserListItemResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await internalUserService.ListAsync(search, page, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<InternalUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await internalUserService.GetAsync(id, cancellationToken), Ok);

    [HttpPut("{id:guid}")]
    [ProducesResponseType<InternalUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, UpdateInternalUserRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await internalUserService.UpdateAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<InternalUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(Guid id, DeactivateInternalUserRequest? request, CancellationToken cancellationToken) =>
        this.ToActionResult(await internalUserService.DeactivateAsync(id, request ?? new DeactivateInternalUserRequest(null), this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<InternalUserResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await internalUserService.ReactivateAsync(id, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType<TemporaryPasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await internalUserService.ResetPasswordAsync(id, this.CurrentUserId(), cancellationToken), Ok);
}
