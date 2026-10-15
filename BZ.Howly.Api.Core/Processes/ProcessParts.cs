using System.Linq.Expressions;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Core.Shared.Exceptions;

namespace BZ.Howly.Api.Core.Processes;

public enum ProcessStatus
{
    InProgress,

    /// <summary>
    /// Temporarily stopped (with a reason): steps, documents and the deadline are frozen until the staff resume or
    /// cancel it. Comments and internal notes are still allowed.
    /// </summary>
    Paused,

    Completed,

    /// <summary>Stopped for good (with a reason).</summary>
    Cancelled,
}

public enum StepStatus
{
    Pending,
    Active,
    Done,
    Skipped,

    /// <summary>The step that was active when the process was cancelled.</summary>
    Cancelled,
}

public enum ProcessEventType
{
    Opened,
    StepAdvanced,
    StepReturned,
    StepSkipped,
    Paused,
    Resumed,
    Cancelled,
    Completed,
    Assigned,
    Commented,

    /// <summary>Advanced while required documents of the step were not approved: the analyst's call, kept for the record.</summary>
    StepAdvancedWithPendingDocuments,

    DocumentUploaded,
    DocumentApproved,
    DocumentRejected,
    DeliverablePublished,

    /// <summary>A note only the staff see: never shown nor told to the customer.</summary>
    InternalNote,

    /// <summary>The title was fixed: the new one in the comment, the old one in the details.</summary>
    Renamed,

    /// <summary>
    /// The active step passed its deadline, told once by the system. The details hold the step owner: only the customer's
    /// own delays are shown to the customer.
    /// </summary>
    StepOverdue,

    /// <summary>The expected return of a paused process arrived: a reminder for the staff (who waits in the details).</summary>
    PauseReturnDue,

    /// <summary>The agency of an agency step changed (the new one in the details).</summary>
    StepAgencyChanged,

    /// <summary>The staff sent an approved document back to review.</summary>
    DocumentReopened,

    /// <summary>The validity of the license or the start of its renewal changed (the expiry, if any, in the details).</summary>
    LicenseChanged,

    /// <summary>The day to start the renewal of the license arrived: a reminder for the staff.</summary>
    RenewalDue,

    /// <summary>The renewal of the license was opened as another process (its title in the details).</summary>
    RenewalOpened,
}

/// <summary>Who did something on a process; the name is copied so the timeline reads well even if it changes later.</summary>
public sealed record Actor(Guid Id, string Name)
{
    /// <summary>Howly itself, for what happens on its own (e.g. a deadline passing).</summary>
    public static readonly Actor System = new(Guid.Empty, "Howly");
}

/// <summary>An action that the process state does not allow (e.g. advancing a cancelled process). Answered as 409.</summary>
public sealed class ProcessRuleException(string message) : ConflictException(message);

/// <summary>A step of a process, copied from the type when the process was opened.</summary>
public sealed class ProcessStep
{
    public Guid Id { get; private set; }
    public int Position { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public StepOwner Owner { get; private set; }
    /// <summary>Agency steps: the agency, copied from the type and changeable while the step is not finished.</summary>
    public Guid? AgencyId { get; private set; }

    /// <summary>Its acronym at the time it was set: renaming the agency later does not rewrite the history.</summary>
    public string? AgencyName { get; private set; }
    public int? DurationDays { get; private set; }
    public bool IsOptional { get; private set; }
    public string? Notes { get; private set; }
    public StepStatus Status { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>When the passed deadline of this run of the step was told; cleared whenever the step starts again.</summary>
    public DateTimeOffset? OverdueNotifiedAt { get; private set; }

    /// <summary>Time the process spent paused during this run of the step; it pushes the deadline forward.</summary>
    public TimeSpan PausedFor { get; private set; }

    public DateTimeOffset? DueAt => StartedAt is { } started && DurationDays is { } days ? started.AddDays(days) + PausedFor : null;

    private ProcessStep() { }

    internal static ProcessStep CopyOf(StepTemplate template) => new()
    {
        Id = Ids.New(),
        Position = template.Position,
        Name = template.Name,
        Description = template.Description,
        Owner = template.Owner,
        AgencyId = template.AgencyId,
        AgencyName = template.AgencyName,
        DurationDays = template.DurationDays,
        IsOptional = template.IsOptional,
        Notes = template.Notes,
        Status = StepStatus.Pending,
    };

    internal void Start(DateTimeOffset now)
    {
        Status = StepStatus.Active;
        StartedAt = now;
        FinishedAt = null;
        OverdueNotifiedAt = null;
        PausedFor = TimeSpan.Zero;
    }

    internal void MarkOverdueNotified(DateTimeOffset now) => OverdueNotifiedAt = now;

    internal void AddPause(TimeSpan paused) => PausedFor += paused;

    internal void SetAgency(Guid agencyId, string agencyName)
    {
        AgencyId = agencyId;
        AgencyName = agencyName;
    }

    internal void Finish(StepStatus status, DateTimeOffset now)
    {
        Status = status;
        FinishedAt = now;
    }

    internal void Reset()
    {
        Status = StepStatus.Pending;
        StartedAt = null;
        FinishedAt = null;
        OverdueNotifiedAt = null;
        PausedFor = TimeSpan.Zero;
    }
}

/// <summary>An entry of the process timeline. Customers see every one of them.</summary>
public sealed class ProcessEvent
{
    public Guid Id { get; private set; }

    /// <summary>Order in the timeline (several events can share the same instant).</summary>
    public int Sequence { get; private set; }

    public ProcessEventType Type { get; private set; }

    /// <summary>The step the event is about, when there is one.</summary>
    public string? StepName { get; private set; }

    /// <summary>Comment, reason or, for <see cref="ProcessEventType.Assigned"/>, the new assignee name.</summary>
    public string? Comment { get; private set; }

    /// <summary>What the event is about beyond the step: the document name, or the pending documents when advancing.</summary>
    public string? Details { get; private set; }

    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = string.Empty;
    public DateTimeOffset At { get; private set; }

    /// <summary>
    /// Everything on the timeline is shared with the customer, except the staff's internal notes, their reminders and
    /// the warnings about steps not with the customer. As an expression so the timeline pages filter in the database.
    /// </summary>
    public static readonly Expression<Func<ProcessEvent, bool>> VisibleToCustomer = e =>
        e.Type != ProcessEventType.InternalNote && e.Type != ProcessEventType.PauseReturnDue && e.Type != ProcessEventType.RenewalDue &&
        (e.Type != ProcessEventType.StepOverdue || e.Details == nameof(StepOwner.Customer));

    private static readonly Func<ProcessEvent, bool> IsVisible = VisibleToCustomer.Compile();

    public bool IsVisibleToCustomer => IsVisible(this);

    private ProcessEvent() { }

    internal static ProcessEvent Of(
        int sequence, ProcessEventType type, Actor actor, DateTimeOffset at, string? stepName = null, string? comment = null, string? details = null) => new()
    {
        Id = Ids.New(),
        Sequence = sequence,
        Type = type,
        StepName = stepName,
        Comment = comment,
        Details = details,
        ActorId = actor.Id,
        ActorName = actor.Name,
        At = at,
    };
}
