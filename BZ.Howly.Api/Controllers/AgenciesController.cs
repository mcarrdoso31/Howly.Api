using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>The agencies the consultancy deals with (IBAMA, CETESB…), kept by its staff. Deactivated, never deleted.</summary>
[ApiController]
[Route("api/agencies")]
[Authorize(Policy = Policies.Internal)]
public sealed class AgenciesController(IAgencyService agencyService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AgencyResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Ok(await agencyService.ListAsync(includeInactive, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AgencyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await agencyService.GetAsync(id, cancellationToken), Ok);

    [HttpPost]
    [ProducesResponseType<AgencyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(AgencyRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await agencyService.CreateAsync(request, cancellationToken), created => CreatedAtAction(nameof(Get), new { id = created.Id }, created));

    [HttpPut("{id:guid}")]
    [ProducesResponseType<AgencyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(Guid id, AgencyRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await agencyService.UpdateAsync(id, request, cancellationToken), Ok);

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<AgencyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await agencyService.SetActiveAsync(id, false, cancellationToken), Ok);

    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<AgencyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await agencyService.SetActiveAsync(id, true, cancellationToken), Ok);
}
