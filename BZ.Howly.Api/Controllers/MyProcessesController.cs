using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>The signed-in customer's processes: read, send documents, comment.</summary>
[ApiController]
[Route("api/me/processes")]
[Authorize(Policy = Policies.Customer)]
public sealed class MyProcessesController(IProcessService processService, IProcessDocumentService documentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<ProcessListItemResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] ProcessStatus? status,
        [FromQuery] StepOwner? waitingOn,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await processService.ListForCustomerAsync(
            this.CurrentUserId(), new ProcessListQuery(search, Status: status, WaitingOn: waitingOn, Page: page, PageSize: pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.GetForCustomerAsync(this.CurrentUserId(), id, cancellationToken), Ok);

    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Comment(Guid id, ProcessCommentRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.CommentForCustomerAsync(this.CurrentUserId(), id, request, cancellationToken), Ok);

    /// <summary>The customer sends (or sends again) a requested or extra document.</summary>
    [HttpPost("{id:guid}/documents/{documentId:guid}/versions")]
    [RequestSizeLimit(ProcessesController.UploadLimit)]
    public async Task<IActionResult> UploadDocument(Guid id, Guid documentId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
            return MissingFile();
        var userId = this.CurrentUserId();
        await using var content = file.OpenReadStream();
        return this.ToActionResult(await documentService.UploadAsync(id, documentId, new IncomingFile(file.FileName, content), userId, userId, cancellationToken), Ok);
    }

    /// <summary>An "other document", described by the customer.</summary>
    [HttpPost("{id:guid}/documents")]
    [RequestSizeLimit(ProcessesController.UploadLimit)]
    public async Task<IActionResult> AddDocument(Guid id, [FromForm] string? name, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
            return MissingFile();
        var userId = this.CurrentUserId();
        await using var content = file.OpenReadStream();
        return this.ToActionResult(
            await documentService.AddAsync(id, DocumentKind.Extra, name, new IncomingFile(file.FileName, content), userId, userId, cancellationToken), Ok);
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/versions/{versionId:guid}/file")]
    public async Task<IActionResult> DownloadDocument(Guid id, Guid documentId, Guid versionId, CancellationToken cancellationToken) =>
        this.Download(await documentService.OpenAsync(id, documentId, versionId, this.CurrentUserId(), cancellationToken));

    /// <summary>The example file of a requested document, to follow when preparing it.</summary>
    [HttpGet("{id:guid}/documents/{documentId:guid}/example")]
    public async Task<IActionResult> DownloadExample(Guid id, Guid documentId, CancellationToken cancellationToken) =>
        this.Download(await documentService.OpenExampleAsync(id, documentId, this.CurrentUserId(), cancellationToken));

    private IActionResult MissingFile() =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["Send the file in the \"file\" field."] }));
}
