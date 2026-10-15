using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>Processes for the staff (every customer, every action) and for customers (their own: read, comment).</summary>
public interface IProcessService
{
    Task<PagedResponse<ProcessListItemResponse>> ListAsync(ProcessListQuery query, CancellationToken cancellationToken = default);

    /// <summary>The home page counts, of <paramref name="assigneeId"/>'s processes when given.</summary>
    Task<ProcessSummaryResponse> SummarizeAsync(Guid? assigneeId, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> OpenAsync(OpenProcessRequest request, Guid actorId, CancellationToken cancellationToken = default);

    // The actions that move the process take the version the caller acted on (If-Match): changed since, 409 "stale_version".
    Task<OperationResult<ProcessResponse>> AdvanceAsync(Guid id, ProcessCommentRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> SkipAsync(Guid id, ProcessCommentRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> ReturnAsync(Guid id, ReturnProcessRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> PauseAsync(Guid id, PauseProcessRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> ResumeAsync(Guid id, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);

    /// <summary>The validity of the license this process obtained and when its renewal should start.</summary>
    Task<OperationResult<ProcessResponse>> SetLicenseAsync(Guid id, SetLicenseRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);

    /// <summary>Opens the renewal of the license as a new process and links it; answers with the new process.</summary>
    Task<OperationResult<ProcessResponse>> OpenRenewalAsync(Guid id, OpenRenewalRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> CancelAsync(Guid id, ProcessReasonRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> AssignAsync(Guid id, AssignProcessRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);

    /// <summary>The agency of an agency step not finished yet (from the active agencies of the catalog).</summary>
    Task<OperationResult<ProcessResponse>> SetStepAgencyAsync(Guid id, Guid stepId, SetStepAgencyRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> CommentAsync(Guid id, ProcessCommentRequest request, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>Fixes the title; the old one stays on the timeline.</summary>
    Task<OperationResult<ProcessResponse>> RenameAsync(Guid id, RenameProcessRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default);

    /// <summary>A note for the staff only.</summary>
    Task<OperationResult<ProcessResponse>> AddInternalNoteAsync(Guid id, ProcessCommentRequest request, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of the timeline, newest first, before the cursor <paramref name="before"/> (from the details or a previous
    /// page); <paramref name="take"/> up to <see cref="ProcessEventPageResponse.MaxSize"/>. 400 for a cursor that is not one.
    /// </summary>
    Task<OperationResult<ProcessEventPageResponse>> ListEventsAsync(Guid id, string? before, int take, CancellationToken cancellationToken = default);

    /// <summary>The signed-in customer's processes; empty when the user is not a customer.</summary>
    Task<PagedResponse<ProcessListItemResponse>> ListForCustomerAsync(Guid userId, ProcessListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Not found when the process belongs to another customer.</summary>
    Task<OperationResult<ProcessResponse>> GetForCustomerAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>The customer comments on their own process; not found when it belongs to another customer.</summary>
    /// <summary>Same pages for the customer, of their own processes, without what only the staff see.</summary>
    Task<OperationResult<ProcessEventPageResponse>> ListEventsForCustomerAsync(Guid userId, Guid id, string? before, int take, CancellationToken cancellationToken = default);

    Task<OperationResult<ProcessResponse>> CommentForCustomerAsync(Guid userId, Guid id, ProcessCommentRequest request, CancellationToken cancellationToken = default);
}
