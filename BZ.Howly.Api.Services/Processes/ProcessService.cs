using System.Globalization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Core.Shared.Exceptions;
using BZ.Howly.Api.Core.Tenancy;

namespace BZ.Howly.Api.Services.Processes;

public sealed class ProcessService(
    IProcessRepository processRepository,
    IProcessDocumentRepository documentRepository,
    INotificationRepository notificationRepository,
    INotificationService notificationService,
    IProcessTypeRepository processTypeRepository,
    ICustomerRepository customerRepository,
    IUserAccountStore userAccountStore,
    IAgencyRepository agencyRepository,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : IProcessService
{
    public const int MaxPageSize = 100;

    public async Task<PagedResponse<ProcessListItemResponse>> ListAsync(ProcessListQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var (items, total) = await processRepository.SearchAsync(query with { Page = page, PageSize = pageSize }, timeProvider.GetUtcNow(), Today(), cancellationToken);
        return new PagedResponse<ProcessListItemResponse>(items, page, pageSize, total);
    }

    public Task<ProcessSummaryResponse> SummarizeAsync(Guid? assigneeId, CancellationToken cancellationToken = default) =>
        processRepository.SummarizeAsync(assigneeId, timeProvider.GetUtcNow(), Today(), cancellationToken);

    public async Task<OperationResult<ProcessResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var process = await processRepository.FindByIdAsync(id, cancellationToken);
        return process is null ? OperationResult<ProcessResponse>.NotFound() : OperationResult<ProcessResponse>.Success(await ToResponseAsync(process, cancellationToken));
    }

    public async Task<OperationResult<ProcessResponse>> OpenAsync(OpenProcessRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        var (process, failure) = await AddOpenedAsync(request, actorId, cancellationToken);
        if (process is null)
            return failure!;

        await processRepository.SaveChangesAsync(cancellationToken);
        return OperationResult<ProcessResponse>.Success(await ToResponseAsync(process, cancellationToken));
    }

    /// <summary>Validates and adds a new process (with its documents and notices) without saving: the caller saves it.</summary>
    private async Task<(Process? Process, OperationResult<ProcessResponse>? Failure)> AddOpenedAsync(
        OpenProcessRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(Process.ValidateTitle(request.Title));

        if (await customerRepository.FindByIdAsync(request.CustomerId, cancellationToken) is null)
            errors["customerId"] = ["Customer not found."];

        var type = await processTypeRepository.FindByIdAsync(request.ProcessTypeId, cancellationToken);
        if (type is not { IsActive: true })
            errors["processTypeId"] = ["Choose an active process type."];

        var assigneeId = request.AssigneeId ?? actorId;
        if (!await IsActiveStaffAsync(assigneeId, cancellationToken))
            errors["assigneeId"] = ["The assignee must be an active staff member."];

        Process? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await processRepository.FindByIdAsync(parentId, cancellationToken);
            // One level only: a subprocess cannot have subprocesses of its own.
            if (parent is null || parent.CustomerId != request.CustomerId || parent.ParentId is not null)
                errors["parentId"] = ["The parent must be a main process of the same customer."];
        }

        if (errors.Count > 0)
            return (null, OperationResult<ProcessResponse>.Invalid(errors));

        Process process;
        try
        {
            process = Process.Open(request.CustomerId, type!, request.Title!, assigneeId, parent, await ActorAsync(actorId, cancellationToken), timeProvider.GetUtcNow());
        }
        catch (ProcessRuleException exception)
        {
            return (null, OperationResult<ProcessResponse>.Conflict(exception.Message));
        }

        await processRepository.AddAsync(process, cancellationToken);

        // Each requested document and deliverable of the type becomes a field of the process, in its step.
        foreach (var (template, step) in type!.Steps.Zip(process.Steps))
        {
            for (var i = 0; i < template.Documents.Count; i++)
                await documentRepository.AddAsync(
                    ProcessDocument.Requested(process.Id, step.Id, template.Documents[i], i), cancellationToken);
            for (var i = 0; i < template.Deliverables.Count; i++)
                await documentRepository.AddAsync(ProcessDocument.Deliverable(process.Id, step.Id, template.Deliverables[i], i), cancellationToken);
        }

        await notificationService.NotifyAsync(process, 0, actorId, cancellationToken);
        return (process, null);
    }

    /// <remarks>
    /// Advancing with required documents not approved is the analyst's call; the timeline keeps their names. Documents
    /// of the step still waiting for the customer are waived: the customer only sends files on their own step.
    /// </remarks>
    public async Task<OperationResult<ProcessResponse>> AdvanceAsync(Guid id, ProcessCommentRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        var documents = await documentRepository.ListByProcessAsync(id, cancellationToken);
        var openSubprocesses = await processRepository.CountOpenSubprocessesAsync(id, cancellationToken);
        return await ActAsync(id, actorId, "comment", (process, actor, now) =>
        {
            var step = process.ActiveStepId;
            process.Advance(actor, request.Comment, now, PendingNames(documents, step), openSubprocesses);
            WaiveStepDocuments(documents, step);
        }, cancellationToken, ifMatch: ifMatch);
    }

    public async Task<OperationResult<ProcessResponse>> SkipAsync(Guid id, ProcessCommentRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        var documents = await documentRepository.ListByProcessAsync(id, cancellationToken);
        var openSubprocesses = await processRepository.CountOpenSubprocessesAsync(id, cancellationToken);
        return await ActAsync(id, actorId, "comment", (process, actor, now) =>
        {
            var step = process.ActiveStepId;
            process.Skip(actor, request.Comment, now, PendingNames(documents, step), openSubprocesses);
            WaiveStepDocuments(documents, step);
        }, cancellationToken, ifMatch: ifMatch);
    }

    /// <remarks>Documents waived in the steps run again are waited for again.</remarks>
    public async Task<OperationResult<ProcessResponse>> ReturnAsync(Guid id, ReturnProcessRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        var documents = await documentRepository.ListByProcessAsync(id, cancellationToken);
        return await ActAsync(id, actorId, "comment", (process, actor, now) =>
        {
            process.ReturnTo(request.StepId, actor, request.Comment, now);
            var target = process.Steps.Single(s => s.Id == request.StepId);
            var reopened = process.Steps.Where(s => s.Position >= target.Position).Select(s => s.Id).ToHashSet();
            foreach (var document in documents.Where(d => d.StepId is { } stepId && reopened.Contains(stepId)))
                document.Restore();
        }, cancellationToken, ifMatch: ifMatch);
    }

    private static List<string> PendingNames(IEnumerable<ProcessDocument> documents, Guid? stepId) =>
        documents.Where(d => d.StepId == stepId && d.IsPending).Select(d => d.Name).ToList();

    private static void WaiveStepDocuments(IEnumerable<ProcessDocument> documents, Guid? stepId)
    {
        foreach (var document in documents.Where(d => d.StepId == stepId))
            document.Waive();
    }

    /// <remarks>
    /// Who it waits on and the expected return are optional; an agency must be an active one of the catalog, and the
    /// expected return a day not before today in the consultancy's time zone.
    /// </remarks>
    public async Task<OperationResult<ProcessResponse>> PauseAsync(Guid id, PauseProcessRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        var today = Today();
        var name = request.WaitingOnName;
        if (request.WaitingOn == PauseWaitingOn.Agency && request.AgencyId is { } agencyId)
        {
            var agency = await agencyRepository.FindAsync(agencyId, cancellationToken);
            if (agency is not { IsActive: true })
                return OperationResult<ProcessResponse>.Invalid("agencyId", "Choose an active agency of the catalog.");
            name = agency.DisplayName;
        }

        var details = request.WaitingOn is null && request.ExpectedReturnOn is null
            ? null
            : new PauseDetails(request.WaitingOn, request.AgencyId, name, request.ExpectedReturnOn);
        var errors = PauseDetails.Validate(details, today);
        if (errors.Count > 0)
            return OperationResult<ProcessResponse>.Invalid(errors);

        return await ActAsync(id, actorId, "reason", (process, actor, now) => process.Pause(actor, request.Reason, now, details, today), cancellationToken, ifMatch: ifMatch);
    }

    /// <summary>Today in the consultancy's time zone.</summary>
    private DateOnly Today() => (tenantContext.Tenant?.Profile ?? TenantProfile.Empty).LocalDate(timeProvider.GetUtcNow());

    public Task<OperationResult<ProcessResponse>> ResumeAsync(Guid id, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default) =>
        ActAsync(id, actorId, "status", (process, actor, now) => process.Resume(actor, now), cancellationToken, ifMatch: ifMatch);

    public Task<OperationResult<ProcessResponse>> SetLicenseAsync(Guid id, SetLicenseRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default) =>
        ActAsync(id, actorId, "renewalStartsOn", (process, actor, now) => process.SetLicense(request.ExpiresOn, request.RenewalStartsOn, actor, now), cancellationToken, ifMatch: ifMatch);

    public async Task<OperationResult<ProcessResponse>> OpenRenewalAsync(Guid id, OpenRenewalRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        var process = await processRepository.FindByIdAsync(id, cancellationToken);
        if (process is null)
            return OperationResult<ProcessResponse>.NotFound();
        ProcessVersion.Require(process, ifMatch);
        if (process.Status == ProcessStatus.Cancelled || process.RenewalProcessId is not null)
            return OperationResult<ProcessResponse>.Conflict("The renewal of this license was already opened, or the process was cancelled.");

        // Same customer and type (the type may have changed since: the renewal follows it as it is now).
        var (renewal, failure) = await AddOpenedAsync(
            new OpenProcessRequest(process.CustomerId, process.ProcessTypeId, request.Title, request.AssigneeId ?? process.AssigneeId, null), actorId, cancellationToken);
        if (renewal is null)
            return failure!;

        var eventsBefore = process.Events.Count;
        process.LinkRenewal(renewal.Id, renewal.Title, await ActorAsync(actorId, cancellationToken), timeProvider.GetUtcNow());
        await notificationService.NotifyAsync(process, eventsBefore, actorId, cancellationToken);
        // One save: the renewal exists only together with its link (a concurrent change of the original saves neither).
        await processRepository.SaveChangesAsync(cancellationToken);
        return OperationResult<ProcessResponse>.Success(await ToResponseAsync(renewal, cancellationToken));
    }

    public async Task<OperationResult<ProcessResponse>> CancelAsync(Guid id, ProcessReasonRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        var openSubprocesses = await processRepository.CountOpenSubprocessesAsync(id, cancellationToken);
        return await ActAsync(id, actorId, "reason", (process, actor, now) => process.Cancel(actor, request.Reason, now, openSubprocesses), cancellationToken, ifMatch: ifMatch);
    }

    public Task<OperationResult<ProcessResponse>> CommentAsync(Guid id, ProcessCommentRequest request, Guid actorId, CancellationToken cancellationToken = default) =>
        ActAsync(id, actorId, "comment", (process, actor, now) => process.Comment(actor, request.Comment, now), cancellationToken);

    public Task<OperationResult<ProcessResponse>> RenameAsync(Guid id, RenameProcessRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default) =>
        ActAsync(id, actorId, "title", (process, actor, now) => process.Rename(actor, request.Title, now), cancellationToken, ifMatch: ifMatch);

    public Task<OperationResult<ProcessResponse>> AddInternalNoteAsync(Guid id, ProcessCommentRequest request, Guid actorId, CancellationToken cancellationToken = default) =>
        ActAsync(id, actorId, "comment", (process, actor, now) => process.AddInternalNote(actor, request.Comment, now), cancellationToken);

    public async Task<OperationResult<ProcessResponse>> AssignAsync(Guid id, AssignProcessRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        var assignee = await userAccountStore.FindByIdAsync(request.AssigneeId, cancellationToken);
        if (assignee is null || !assignee.IsActive || !assignee.HasAnyRole(Roles.Internal))
            return OperationResult<ProcessResponse>.Invalid("assigneeId", "The assignee must be an active staff member.");

        return await ActAsync(id, actorId, "assigneeId",
            (process, actor, now) => process.Assign(assignee.Id, assignee.DisplayName, actor, now), cancellationToken, ifMatch: ifMatch);
    }

    public async Task<OperationResult<ProcessResponse>> SetStepAgencyAsync(
        Guid id, Guid stepId, SetStepAgencyRequest request, Guid actorId, string? ifMatch = null, CancellationToken cancellationToken = default)
    {
        var agency = await agencyRepository.FindAsync(request.AgencyId, cancellationToken);
        if (agency is not { IsActive: true })
            return OperationResult<ProcessResponse>.Invalid("agencyId", "Choose an active agency of the catalog.");

        return await ActAsync(id, actorId, "agencyId",
            (process, actor, now) => process.SetStepAgency(stepId, agency.Id, agency.DisplayName, actor, now), cancellationToken, ifMatch: ifMatch);
    }

    public async Task<PagedResponse<ProcessListItemResponse>> ListForCustomerAsync(Guid userId, ProcessListQuery query, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByUserIdAsync(userId, cancellationToken);
        if (customer is null)
            return new PagedResponse<ProcessListItemResponse>([], 1, query.PageSize, 0);

        return await ListAsync(query with { CustomerId = customer.Id, AssigneeId = null }, cancellationToken);
    }

    public async Task<OperationResult<ProcessResponse>> GetForCustomerAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByUserIdAsync(userId, cancellationToken);
        var process = await processRepository.FindByIdAsync(id, cancellationToken);
        if (customer is null || process is null || process.CustomerId != customer.Id)
            return OperationResult<ProcessResponse>.NotFound();

        return OperationResult<ProcessResponse>.Success(await ToResponseAsync(process, cancellationToken, forCustomer: true));
    }

    public async Task<OperationResult<ProcessResponse>> CommentForCustomerAsync(
        Guid userId, Guid id, ProcessCommentRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByUserIdAsync(userId, cancellationToken);
        var process = await processRepository.FindByIdAsync(id, cancellationToken);
        if (customer is null || process is null || process.CustomerId != customer.Id)
            return OperationResult<ProcessResponse>.NotFound();

        return await ActAsync(id, userId, "comment", (p, actor, now) => p.Comment(actor, request.Comment, now), cancellationToken, forCustomer: true);
    }

    /// <summary>
    /// Loads, applies a state change, saves: rule violations become 409, a missing comment/reason 400. With
    /// <paramref name="ifMatch"/>, a process changed since the caller loaded it is refused (409 stale) before anything.
    /// </summary>
    private async Task<OperationResult<ProcessResponse>> ActAsync(
        Guid id, Guid actorId, string field, Action<Process, Actor, DateTimeOffset> action, CancellationToken cancellationToken, bool forCustomer = false,
        string? ifMatch = null)
    {
        var process = await processRepository.FindByIdAsync(id, cancellationToken);
        if (process is null)
            return OperationResult<ProcessResponse>.NotFound();
        ProcessVersion.Require(process, ifMatch);

        var eventsBefore = process.Events.Count;
        var assigneeBefore = process.AssigneeId;
        try
        {
            action(process, await ActorAsync(actorId, cancellationToken), timeProvider.GetUtcNow());
        }
        catch (ProcessRuleException exception)
        {
            return OperationResult<ProcessResponse>.Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return OperationResult<ProcessResponse>.Invalid(field, exception.Message);
        }

        // Whoever just lost the process is told too: otherwise it would silently leave their list.
        var previousAssignee = process.AssigneeId != assigneeBefore ? assigneeBefore : (Guid?)null;
        await notificationService.NotifyAsync(process, eventsBefore, actorId, previousAssignee, cancellationToken);
        await processRepository.SaveChangesAsync(cancellationToken);
        return OperationResult<ProcessResponse>.Success(await ToResponseAsync(process, cancellationToken, forCustomer));
    }

    private async Task<Actor> ActorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userAccountStore.FindByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException("The signed-in user no longer exists.");
        return new Actor(user.Id, user.DisplayName);
    }

    private async Task<bool> IsActiveStaffAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userAccountStore.FindByIdAsync(userId, cancellationToken);
        return user is { IsActive: true } && user.HasAnyRole(Roles.Internal);
    }

    /// <summary>The customer's view leaves out the staff's internal notes.</summary>
    private async Task<ProcessResponse> ToResponseAsync(Process process, CancellationToken cancellationToken, bool forCustomer = false)
    {
        var customer = await customerRepository.FindByIdAsync(process.CustomerId, cancellationToken);
        var customerUser = customer is null ? null : await userAccountStore.FindByIdAsync(customer.UserId, cancellationToken);
        var assignee = await userAccountStore.FindByIdAsync(process.AssigneeId, cancellationToken);
        var parent = process.ParentId is { } parentId ? await processRepository.FindByIdAsync(parentId, cancellationToken) : null;
        var seenByCustomer = customer is null
            ? new Dictionary<Guid, DateTimeOffset>()
            : await notificationRepository.ReadAtByEventAsync(process.Id, customer.UserId, cancellationToken);
        var children = process.ParentId is null
            ? (await processRepository.SearchAsync(new ProcessListQuery(ParentId: process.Id, PageSize: MaxPageSize), timeProvider.GetUtcNow(), Today(), cancellationToken)).Items
            : [];
        // Only the newest page of the timeline: long-lived processes would make every answer heavier. Older ones via …/events.
        var visible = process.Events.Where(e => !forCustomer || e.IsVisibleToCustomer).ToList();
        var newest = visible.TakeLast(ProcessEventPageResponse.DefaultSize).ToList();
        var hasMoreEvents = visible.Count > newest.Count;

        return new ProcessResponse(
            process.Id,
            process.Title,
            new PersonResponse(process.CustomerId, customer?.DisplayName ?? string.Empty, customerUser?.AvatarVersion),
            process.ProcessTypeId,
            process.ProcessTypeName,
            process.Status,
            process.StatusReason,
            new PersonResponse(process.AssigneeId, assignee?.DisplayName ?? string.Empty, assignee?.AvatarVersion),
            parent is null ? null : new ProcessParentResponse(parent.Id, parent.Title),
            process.WaitingOn,
            process.ActiveStepDueAt,
            process.CreatedAt,
            process.UpdatedAt,
            process.CompletedAt,
            process.Steps.Select(ProcessStepResponse.From).ToList(),
            newest.Select(e => ProcessEventResponse.From(e, seenByCustomer, customer?.UserId)).ToList(),
            children,
            (await documentRepository.ListByProcessAsync(process.Id, cancellationToken)).Select(ProcessDocumentResponse.From).ToList(),
            PauseResponse.From(process),
            LicenseResponse.From(process),
            ProcessVersion.Of(process),
            hasMoreEvents,
            hasMoreEvents ? EventsCursor(newest[0]) : null);
    }

    public Task<OperationResult<ProcessEventPageResponse>> ListEventsAsync(Guid id, string? before, int take, CancellationToken cancellationToken = default) =>
        PageEventsAsync(id, null, before, take, cancellationToken);

    public async Task<OperationResult<ProcessEventPageResponse>> ListEventsForCustomerAsync(
        Guid userId, Guid id, string? before, int take, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByUserIdAsync(userId, cancellationToken);
        return customer is null
            ? OperationResult<ProcessEventPageResponse>.NotFound()
            : await PageEventsAsync(id, customer.Id, before, take, cancellationToken);
    }

    /// <summary>
    /// A page of the timeline before <paramref name="before"/> (a cursor of the details or of an earlier page), newest
    /// first. For a customer (<paramref name="customerId"/>): only their own processes and what they see of them.
    /// </summary>
    private async Task<OperationResult<ProcessEventPageResponse>> PageEventsAsync(
        Guid id, Guid? customerId, string? before, int take, CancellationToken cancellationToken)
    {
        int? beforeSequence = null;
        if (before is not null)
        {
            if (!int.TryParse(before, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
                return OperationResult<ProcessEventPageResponse>.Invalid("before", "Use the cursor of the process or of the previous page.");
            beforeSequence = sequence;
        }

        var ownerId = await processRepository.FindCustomerIdAsync(id, cancellationToken);
        if (ownerId is null || (customerId is not null && ownerId != customerId))
            return OperationResult<ProcessEventPageResponse>.NotFound();

        var size = Math.Clamp(take, 1, ProcessEventPageResponse.MaxSize);
        // One more than asked tells whether older ones remain, without counting them.
        var events = await processRepository.ListEventsAsync(id, beforeSequence, size + 1, visibleToCustomerOnly: customerId is not null, cancellationToken);
        var page = events.Take(size).ToList();

        var customer = await customerRepository.FindByIdAsync(ownerId.Value, cancellationToken);
        var seenByCustomer = customer is null
            ? new Dictionary<Guid, DateTimeOffset>()
            : await notificationRepository.ReadAtByEventAsync(id, customer.UserId, cancellationToken);
        return OperationResult<ProcessEventPageResponse>.Success(new ProcessEventPageResponse(
            page.Select(e => ProcessEventResponse.From(e, seenByCustomer, customer?.UserId)).ToList(),
            events.Count > size ? EventsCursor(page[^1]) : null));
    }

    /// <summary>Opaque to the clients: the position of the event on the timeline.</summary>
    private static string EventsCursor(ProcessEvent oldestShown) => oldestShown.Sequence.ToString(CultureInfo.InvariantCulture);
}
