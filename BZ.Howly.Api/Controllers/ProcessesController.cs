using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>Processes of every customer, for the staff. Invalid transitions (e.g. advancing a paused process) answer 409.</summary>
[ApiController]
[Route("api/processes")]
[Authorize(Policy = Policies.Internal)]
public sealed class ProcessesController(IProcessService processService, IProcessDocumentService documentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<ProcessListItemResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] Guid? customerId,
        [FromQuery] ProcessStatus? status,
        [FromQuery] StepOwner? waitingOn,
        [FromQuery] Guid? assigneeId,
        [FromQuery] bool overdue = false,
        [FromQuery] Guid? parentId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await processService.ListAsync(
            new ProcessListQuery(search, customerId, status, waitingOn, assigneeId, overdue, parentId, page, pageSize), cancellationToken));

    /// <summary>The home page counts: open, late, and who they wait on. <paramref name="mine"/> keeps the caller's own.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<ProcessSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] bool mine = false, CancellationToken cancellationToken = default) =>
        Ok(await processService.SummarizeAsync(mine ? this.CurrentUserId() : null, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.GetAsync(id, cancellationToken), Ok);

    [HttpPost]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Open(OpenProcessRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(
            await processService.OpenAsync(request, this.CurrentUserId(), cancellationToken),
            opened => CreatedAtAction(nameof(Get), new { id = opened.Id }, opened));

    [HttpPost("{id:guid}/advance")]
    public async Task<IActionResult> Advance(Guid id, ProcessCommentRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.AdvanceAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/skip")]
    public async Task<IActionResult> Skip(Guid id, ProcessCommentRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.SkipAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/return")]
    public async Task<IActionResult> Return(Guid id, ReturnProcessRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.ReturnAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id, ProcessReasonRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.PauseAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.ResumeAsync(id, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, ProcessReasonRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.CancelAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, AssignProcessRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.AssignAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    /// <summary>Which agency an agency step is with (e.g. FEAM instead of the type's default CETESB), until it finishes.</summary>
    [HttpPut("{id:guid}/steps/{stepId:guid}/agency")]
    [ProducesResponseType<ProcessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetStepAgency(Guid id, Guid stepId, SetStepAgencyRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.SetStepAgencyAsync(id, stepId, request, this.CurrentUserId(), cancellationToken), Ok);

    // ---- documents ----

    /// <summary>A new version of a document (on behalf of the customer, or a deliverable).</summary>
    [HttpPost("{id:guid}/documents/{documentId:guid}/versions")]
    [RequestSizeLimit(UploadLimit)]
    public async Task<IActionResult> UploadDocument(Guid id, Guid documentId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
            return MissingFile();
        await using var content = file.OpenReadStream();
        return this.ToActionResult(await documentService.UploadAsync(id, documentId, new IncomingFile(file.FileName, content), this.CurrentUserId(), null, cancellationToken), Ok);
    }

    /// <summary>A document nobody asked for: kind Extra (default) or Deliverable, with a name.</summary>
    [HttpPost("{id:guid}/documents")]
    [RequestSizeLimit(UploadLimit)]
    public async Task<IActionResult> AddDocument(
        Guid id, [FromForm] string? name, IFormFile? file, CancellationToken cancellationToken, [FromForm] DocumentKind kind = DocumentKind.Extra)
    {
        if (file is null)
            return MissingFile();
        await using var content = file.OpenReadStream();
        return this.ToActionResult(await documentService.AddAsync(id, kind, name, new IncomingFile(file.FileName, content), this.CurrentUserId(), null, cancellationToken), Ok);
    }

    [HttpPost("{id:guid}/documents/{documentId:guid}/approve")]
    public async Task<IActionResult> ApproveDocument(Guid id, Guid documentId, CancellationToken cancellationToken) =>
        this.ToActionResult(await documentService.ApproveAsync(id, documentId, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPost("{id:guid}/documents/{documentId:guid}/reject")]
    public async Task<IActionResult> RejectDocument(Guid id, Guid documentId, RejectDocumentRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await documentService.RejectAsync(id, documentId, request.Reason, this.CurrentUserId(), cancellationToken), Ok);

    /// <summary>Sends an approved document back to review; only the staff can (the customer never reopens it).</summary>
    [HttpPost("{id:guid}/documents/{documentId:guid}/reopen")]
    public async Task<IActionResult> ReopenDocument(Guid id, Guid documentId, CancellationToken cancellationToken) =>
        this.ToActionResult(await documentService.ReopenAsync(id, documentId, this.CurrentUserId(), cancellationToken), Ok);

    [HttpGet("{id:guid}/documents/{documentId:guid}/versions/{versionId:guid}/file")]
    public async Task<IActionResult> DownloadDocument(Guid id, Guid documentId, Guid versionId, CancellationToken cancellationToken) =>
        this.Download(await documentService.OpenAsync(id, documentId, versionId, null, cancellationToken));

    /// <summary>Room for a 25 MB file plus the multipart envelope.</summary>
    internal const long UploadLimit = UploadPolicy.MaxBytes + 1024 * 1024;

    private IActionResult MissingFile() =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["Send the file in the \"file\" field."] }));

    [HttpPost("{id:guid}/comments")]
    public async Task<IActionResult> Comment(Guid id, ProcessCommentRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.CommentAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    [HttpPut("{id:guid}/title")]
    public async Task<IActionResult> Rename(Guid id, RenameProcessRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.RenameAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);

    /// <summary>A note only the staff see.</summary>
    [HttpPost("{id:guid}/notes")]
    public async Task<IActionResult> AddInternalNote(Guid id, ProcessCommentRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await processService.AddInternalNoteAsync(id, request, this.CurrentUserId(), cancellationToken), Ok);
}
