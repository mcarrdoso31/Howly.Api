using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>Templates of processes (steps, owners, deadlines), configured by the staff.</summary>
[ApiController]
[Route("api/process-types")]
[Authorize(Policy = Policies.Internal)]
public sealed class ProcessTypesController(IProcessTypeService processTypeService, IDocumentExampleService exampleService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProcessTypeListItemResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Ok(await processTypeService.ListAsync(includeInactive, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProcessTypeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await processTypeService.GetAsync(id, cancellationToken), Ok);

    [HttpPost]
    [ProducesResponseType<ProcessTypeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(ProcessTypeRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(
            await processTypeService.CreateAsync(request, cancellationToken),
            created => CreatedAtAction(nameof(Get), new { id = created.Id }, created));

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProcessTypeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, ProcessTypeRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processTypeService.UpdateAsync(id, request, cancellationToken), Ok);

    /// <summary>An example file for a requested document; its id then goes in the document ("exampleId") when saving the type.</summary>
    [HttpPost("examples")]
    [RequestSizeLimit(ProcessesController.UploadLimit)]
    [ProducesResponseType<DocumentExampleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadExample(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["Send the file in the \"file\" field."] }));
        await using var content = file.OpenReadStream();
        return this.ToActionResult(
            await exampleService.UploadAsync(new IncomingFile(file.FileName, content), this.CurrentUserId(), cancellationToken),
            uploaded => CreatedAtAction(nameof(DownloadExample), new { exampleId = uploaded.Id }, uploaded));
    }

    [HttpGet("examples/{exampleId:guid}")]
    public async Task<IActionResult> DownloadExample(Guid exampleId, CancellationToken cancellationToken) =>
        this.Download(await exampleService.OpenAsync(exampleId, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<ProcessTypeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await processTypeService.SetActiveAsync(id, false, cancellationToken), Ok);

    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<ProcessTypeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await processTypeService.SetActiveAsync(id, true, cancellationToken), Ok);
}
