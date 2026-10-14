using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Processes;

/// <summary>
/// A process of a customer, opened by the staff from a <see cref="ProcessType"/>. Its steps run one at a time; the active
/// step tells who the process is waiting on, which is kept on the process itself so lists can filter by it.
/// </summary>
public sealed class Process
{
    public const int MaxTitleLength = 200;
    public const int MaxCommentLength = 2000;

    private readonly List<ProcessStep> _steps = [];
    private readonly List<ProcessEvent> _events = [];

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ProcessTypeId { get; private set; }

    /// <summary>Copied when opened: renaming the type later does not rename existing processes.</summary>
    public string ProcessTypeName { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    /// <summary>The staff member responsible for the process.</summary>
    public Guid AssigneeId { get; private set; }

    /// <summary>Set on subprocesses (e.g. each license of a PGRS).</summary>
    public Guid? ParentId { get; private set; }

    public ProcessStatus Status { get; private set; }

    /// <summary>Why the process is paused or cancelled; shown to the customer.</summary>
    public string? StatusReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Since when the process is paused; the time paused is added to the deadline of the active step on resume.</summary>
    public DateTimeOffset? PausedAt { get; private set; }

    /// <summary>Changes on every change of the process: saving over a version someone else already changed is refused (409).</summary>
    public Guid Version { get; private set; }

    /// <summary>Owner of the active step while in progress; null when paused, completed or cancelled.</summary>
    public StepOwner? WaitingOn { get; private set; }

    public Guid? ActiveStepId { get; private set; }
    public string? ActiveStepName { get; private set; }
    public DateTimeOffset? ActiveStepStartedAt { get; private set; }
    public DateTimeOffset? ActiveStepDueAt { get; private set; }

    public IReadOnlyList<ProcessStep> Steps => _steps.OrderBy(s => s.Position).ToList();
    public IReadOnlyList<ProcessEvent> Events => _events.OrderBy(e => e.Sequence).ToList();
    public ProcessStep? ActiveStep => _steps.SingleOrDefault(s => s.Status == StepStatus.Active);

    private Process() { }

    public static IReadOnlyDictionary<string, string[]> ValidateTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength
            ? new Dictionary<string, string[]> { ["title"] = [$"Title is required and must have at most {MaxTitleLength} characters."] }
            : new Dictionary<string, string[]>();

    /// <param name="parent">Opens a subprocess: the parent must be a main process of the same customer, in progress.</param>
    public static Process Open(Guid customerId, ProcessType type, string title, Guid assigneeId, Process? parent, Actor openedBy, DateTimeOffset now)
    {
        if (ValidateTitle(title).Count > 0)
            throw new ArgumentException("Invalid title.", nameof(title));
        if (!type.IsActive)
            throw new ProcessRuleException("Processes cannot be opened from an inactive type.");
        if (parent is not null && (parent.CustomerId != customerId || parent.ParentId is not null))
            throw new ProcessRuleException("The parent must be a main process of the same customer.");
        if (parent is not null && parent.Status != ProcessStatus.InProgress)
            throw new ProcessRuleException("Subprocesses can only be opened under a process in progress.");

        var process = new Process
        {
            Id = Ids.New(),
            CustomerId = customerId,
            ProcessTypeId = type.Id,
            ProcessTypeName = type.Name,
            Title = title.Trim(),
            AssigneeId = assigneeId,
            ParentId = parent?.Id,
            Status = ProcessStatus.InProgress,
            CreatedAt = now,
        };
        process._steps.AddRange(type.Steps.Select(ProcessStep.CopyOf));
        process._steps.OrderBy(s => s.Position).First().Start(now);
        process.Record(ProcessEventType.Opened, openedBy, now);
        process.RefreshActiveStep(now);
        return process;
    }

    /// <summary>
    /// Finishes the active step and starts the next one; after the last one the process is completed. Advancing with
    /// required documents not approved yet is allowed (the analyst's call) and recorded with their names.
    /// </summary>
    /// <param name="openSubprocesses">Subprocesses still open: a process cannot be completed while it has any.</param>
    public void Advance(Actor actor, string? comment, DateTimeOffset now, IReadOnlyList<string>? pendingDocuments = null, int openSubprocesses = 0)
    {
        var active = RequireActiveStep();
        RequireNoOpenSubprocessesToFinish(active, openSubprocesses);
        active.Finish(StepStatus.Done, now);
        if (pendingDocuments is { Count: > 0 })
            Record(ProcessEventType.StepAdvancedWithPendingDocuments, actor, now, active.Name, Clean(comment), string.Join(", ", pendingDocuments));
        else
            Record(ProcessEventType.StepAdvanced, actor, now, active.Name, Clean(comment));
        StartNextAfter(active, actor, now);
    }

    private static readonly HashSet<ProcessEventType> DocumentEvents =
    [
        ProcessEventType.DocumentUploaded, ProcessEventType.DocumentApproved, ProcessEventType.DocumentRejected, ProcessEventType.DeliverablePublished,
        ProcessEventType.DocumentReopened,
    ];

    /// <summary>Puts something that happened to a document of this process on the timeline.</summary>
    public void RecordDocumentEvent(ProcessEventType type, string documentName, Actor actor, DateTimeOffset now, string? comment = null)
    {
        if (!DocumentEvents.Contains(type))
            throw new ArgumentException($"{type} is not a document event.", nameof(type));

        Record(type, actor, now, comment: Clean(comment), details: documentName);
        Touch(now);
    }

    /// <summary>Documents only change while the process is in progress: a paused process is frozen and a finished one is closed.</summary>
    public void RequireOpenForDocuments()
    {
        if (Status == ProcessStatus.Paused)
            throw new ProcessRuleException("The process is paused: resume it before changing documents.");
        if (Status != ProcessStatus.InProgress)
            throw new ProcessRuleException("Finished processes do not take document changes.");
    }

    /// <summary>
    /// The customer sends files only while the active step is theirs, and only for that step (or extras): requested
    /// documents of other steps, approved documents (only the staff reopen them) and deliverables are refused.
    /// </summary>
    /// <param name="document">The document getting a new version; null for a new extra document.</param>
    public void RequireCustomerCanSend(ProcessDocument? document)
    {
        RequireOpenForDocuments();
        if (WaitingOn != StepOwner.Customer)
            throw new ProcessRuleException("Documents can only be sent while the current step is with the customer.");
        if (document is null)
            return;
        if (document.Kind == DocumentKind.Deliverable)
            throw new ProcessRuleException("Deliverables are published by the staff.");
        if (document.Kind == DocumentKind.Requested && document.StepId != ActiveStepId)
            throw new ProcessRuleException("This document belongs to another step.");
        if (document.Status == DocumentStatus.Approved)
            throw new ProcessRuleException("Approved documents can only be reopened by the staff.");
    }

    /// <summary>Skips the active step; only optional steps can be skipped. Pending required documents are recorded with their names.</summary>
    public void Skip(Actor actor, string? comment, DateTimeOffset now, IReadOnlyList<string>? pendingDocuments = null, int openSubprocesses = 0)
    {
        var active = RequireActiveStep();
        if (!active.IsOptional)
            throw new ProcessRuleException("Only optional steps can be skipped.");
        RequireNoOpenSubprocessesToFinish(active, openSubprocesses);

        active.Finish(StepStatus.Skipped, now);
        Record(ProcessEventType.StepSkipped, actor, now, active.Name, Clean(comment),
            pendingDocuments is { Count: > 0 } ? string.Join(", ", pendingDocuments) : null);
        StartNextAfter(active, actor, now);
    }

    /// <summary>Reopens an earlier step (e.g. the agency asked for corrections); every later step goes back to pending.</summary>
    public void ReturnTo(Guid stepId, Actor actor, string? comment, DateTimeOffset now)
    {
        var reason = Required(comment, nameof(comment));
        var active = RequireActiveStep();
        var target = _steps.SingleOrDefault(s => s.Id == stepId);
        if (target is null || target.Position >= active.Position)
            throw new ProcessRuleException("A process can only return to an earlier step.");

        foreach (var step in _steps.Where(s => s.Position > target.Position))
            step.Reset();
        target.Start(now);

        Record(ProcessEventType.StepReturned, actor, now, target.Name, reason);
        RefreshActiveStep(now);
    }

    public void Pause(Actor actor, string? reason, DateTimeOffset now)
    {
        var text = Required(reason, nameof(reason));
        if (Status != ProcessStatus.InProgress)
            throw new ProcessRuleException("Only processes in progress can be paused.");

        Status = ProcessStatus.Paused;
        StatusReason = text;
        PausedAt = now;
        Record(ProcessEventType.Paused, actor, now, comment: text);
        RefreshActiveStep(now);
    }

    /// <summary>Back to work on the same step; the time paused does not count against its deadline.</summary>
    public void Resume(Actor actor, DateTimeOffset now)
    {
        if (Status != ProcessStatus.Paused)
            throw new ProcessRuleException("Only paused processes can be resumed.");

        if (PausedAt is { } pausedAt && now > pausedAt)
            ActiveStep?.AddPause(now - pausedAt);
        Status = ProcessStatus.InProgress;
        StatusReason = null;
        PausedAt = null;
        Record(ProcessEventType.Resumed, actor, now);
        RefreshActiveStep(now);
    }

    /// <param name="openSubprocesses">Subprocesses still open: they must be finished or cancelled first.</param>
    public void Cancel(Actor actor, string? reason, DateTimeOffset now, int openSubprocesses = 0)
    {
        var text = Required(reason, nameof(reason));
        if (Status is not (ProcessStatus.InProgress or ProcessStatus.Paused))
            throw new ProcessRuleException("Only open processes can be cancelled.");
        if (openSubprocesses > 0)
            throw new ProcessRuleException("Cancel or finish the subprocesses first.");

        ActiveStep?.Finish(StepStatus.Cancelled, now);
        Status = ProcessStatus.Cancelled;
        StatusReason = text;
        PausedAt = null;
        Record(ProcessEventType.Cancelled, actor, now, comment: text);
        RefreshActiveStep(now);
    }

    /// <summary>Open processes (paused included, e.g. when someone leaves the team) change hands; the same person again changes nothing.</summary>
    public void Assign(Guid assigneeId, string assigneeName, Actor actor, DateTimeOffset now)
    {
        if (assigneeId == Guid.Empty)
            throw new ArgumentException("Choose who is responsible for the process.", nameof(assigneeId));
        if (Status is not (ProcessStatus.InProgress or ProcessStatus.Paused))
            throw new ProcessRuleException("Finished processes cannot change hands.");
        if (assigneeId == AssigneeId)
            return;

        AssigneeId = assigneeId;
        Record(ProcessEventType.Assigned, actor, now, comment: assigneeName);
        Touch(now);
    }

    /// <summary>A note on the timeline, while the process is open (paused included); closed once it is completed or cancelled.</summary>
    public void Comment(Actor actor, string? text, DateTimeOffset now)
    {
        if (Status is ProcessStatus.Completed or ProcessStatus.Cancelled)
            throw new ProcessRuleException("Finished processes do not take comments.");

        Record(ProcessEventType.Commented, actor, now, comment: Required(text, nameof(text)));
        Touch(now);
    }

    /// <summary>
    /// Tells (once per run of the step) that the active step passed its deadline. False when it is not late, has no
    /// deadline, was already told, or the process is not in progress.
    /// </summary>
    public bool MarkOverdue(DateTimeOffset now)
    {
        var active = Status == ProcessStatus.InProgress ? ActiveStep : null;
        if (active?.DueAt is not { } due || due > now || active.OverdueNotifiedAt is not null)
            return false;

        active.MarkOverdueNotified(now);
        Record(ProcessEventType.StepOverdue, Actor.System, now, active.Name, details: active.Owner.ToString());
        // A new version, but not "updated": a warning from the system does not move the process up the lists.
        Version = Guid.NewGuid();
        return true;
    }

    /// <summary>Fixes the title in any status; the old one stays on the timeline. Same title: nothing happens.</summary>
    public void Rename(Actor actor, string? title, DateTimeOffset now)
    {
        if (ValidateTitle(title).Count > 0)
            throw new ArgumentException($"Title is required and must have at most {MaxTitleLength} characters.", nameof(title));

        var newTitle = title!.Trim();
        if (newTitle == Title)
            return;

        Record(ProcessEventType.Renamed, actor, now, comment: newTitle, details: Title);
        Title = newTitle;
        Touch(now);
    }

    /// <summary>A note for the staff only (the customer never sees it); allowed in any status, even after the end.</summary>
    public void AddInternalNote(Actor actor, string? text, DateTimeOffset now)
    {
        Record(ProcessEventType.InternalNote, actor, now, comment: Required(text, nameof(text)));
        Touch(now);
    }

    private ProcessStep RequireActiveStep()
    {
        if (Status != ProcessStatus.InProgress)
            throw new ProcessRuleException("Steps only move while the process is in progress.");
        return ActiveStep ?? throw new ProcessRuleException("The process has no active step.");
    }

    /// <summary>Finishing the last step completes the process, which needs every subprocess finished first.</summary>
    private void RequireNoOpenSubprocessesToFinish(ProcessStep active, int openSubprocesses)
    {
        var isLast = _steps.All(s => s.Position <= active.Position);
        if (isLast && openSubprocesses > 0)
            throw new ProcessRuleException("Cancel or finish the subprocesses first.");
    }

    private void StartNextAfter(ProcessStep finished, Actor actor, DateTimeOffset now)
    {
        var next = _steps.Where(s => s.Position > finished.Position).OrderBy(s => s.Position).FirstOrDefault();
        if (next is null)
        {
            Status = ProcessStatus.Completed;
            CompletedAt = now;
            Record(ProcessEventType.Completed, actor, now);
        }
        else
        {
            next.Start(now);
        }

        RefreshActiveStep(now);
    }

    /// <summary>Keeps the list/filter fields in line with the steps and the status.</summary>
    private void RefreshActiveStep(DateTimeOffset now)
    {
        var active = Status == ProcessStatus.InProgress ? ActiveStep : null;
        WaitingOn = active?.Owner;
        ActiveStepId = active?.Id;
        ActiveStepName = active?.Name;
        ActiveStepStartedAt = active?.StartedAt;
        ActiveStepDueAt = active?.DueAt;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    private void Record(ProcessEventType type, Actor actor, DateTimeOffset at, string? stepName = null, string? comment = null, string? details = null) =>
        _events.Add(ProcessEvent.Of(_events.Count, type, actor, at, stepName, comment, details));

    private static string? Clean(string? text)
    {
        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (trimmed?.Length > MaxCommentLength)
            throw new ArgumentException($"Comments must have at most {MaxCommentLength} characters.");
        return trimmed;
    }

    private static string Required(string? text, string parameter) =>
        Clean(text) ?? throw new ArgumentException("A comment or reason is required.", parameter);
}
