using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.Notifications;

/// <summary>
/// Something that happened on a process, told to one person tied to it (the customer or the assignee).
/// One row per person, so each one's reading is recorded on its own.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; private set; }

    /// <summary>Who receives it.</summary>
    public Guid UserId { get; private set; }

    public Guid ProcessId { get; private set; }
    public string ProcessTitle { get; private set; } = string.Empty;

    /// <summary>The timeline event it tells about; lets the staff see on the timeline when the customer saw it.</summary>
    public Guid EventId { get; private set; }

    /// <summary>Order of the event in the process, to keep notifications of the same instant in order.</summary>
    public int EventSequence { get; private set; }

    public ProcessEventType Type { get; private set; }
    public string? StepName { get; private set; }

    /// <summary>The document name, or the new assignee name, depending on the type.</summary>
    public string? Details { get; private set; }

    public string ActorName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the person first read it; null while unread.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    private Notification() { }

    public static Notification For(Guid userId, Process process, ProcessEvent processEvent) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ProcessId = process.Id,
        ProcessTitle = process.Title,
        EventId = processEvent.Id,
        EventSequence = processEvent.Sequence,
        Type = processEvent.Type,
        StepName = processEvent.StepName,
        Details = processEvent.Type == ProcessEventType.Assigned ? processEvent.Comment : processEvent.Details,
        ActorName = processEvent.ActorName,
        CreatedAt = processEvent.At,
    };

    /// <summary>Reading again does not move the time: the first reading is the one on record.</summary>
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    /// <summary>
    /// Everyone tied to the process (its customer and its assignee, plus the previous assignee when it just changed
    /// hands), except whoever did the action.
    /// </summary>
    public static IReadOnlyList<Guid> Recipients(Guid? customerUserId, Guid assigneeId, Guid actorId, Guid? previousAssigneeId = null) =>
        new[] { customerUserId, assigneeId, previousAssigneeId }
            .OfType<Guid>()
            .Where(id => id != actorId)
            .Distinct()
            .ToList();
}
