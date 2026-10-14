using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Processes;

public enum DocumentKind
{
    /// <summary>Asked by a step of the process type; the customer uploads it and the staff review it.</summary>
    Requested,

    /// <summary>Sent on top of what was asked ("other documents"), with a description; reviewed like requested ones.</summary>
    Extra,

    /// <summary>Delivered by the staff (license, protocol, payment slip); published without review.</summary>
    Deliverable,
}

public enum DocumentStatus
{
    AwaitingUpload,
    InReview,
    Approved,
    Rejected,
    Published,

    /// <summary>
    /// Its step ended without it (advanced or skipped): nobody waits for it anymore. Returning to the step asks for it again.
    /// </summary>
    Waived,
}

/// <summary>A file already saved in the file storage, as the document version will reference it.</summary>
public sealed record StoredFile(string Key, string FileName, long Size);

/// <summary>One upload of a document; reviews apply to it.</summary>
public sealed class DocumentVersion
{
    public Guid Id { get; private set; }
    public int Number { get; private set; }
    public string FileKey { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long Size { get; private set; }
    public Guid UploadedById { get; private set; }
    public string UploadedByName { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }
    public DocumentStatus Status { get; private set; }
    public string? ReviewReason { get; private set; }
    public string? ReviewedByName { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    private DocumentVersion() { }

    internal static DocumentVersion Of(int number, StoredFile file, Actor actor, DateTimeOffset now, DocumentStatus status) => new()
    {
        Id = Ids.New(),
        Number = number,
        FileKey = file.Key,
        FileName = file.FileName,
        Size = file.Size,
        UploadedById = actor.Id,
        UploadedByName = actor.Name,
        UploadedAt = now,
        Status = status,
    };

    internal void Review(DocumentStatus status, Actor actor, string? reason, DateTimeOffset now)
    {
        Status = status;
        ReviewReason = reason;
        ReviewedByName = actor.Name;
        ReviewedAt = now;
    }

    /// <summary>Back to review: the earlier review stays on the timeline only.</summary>
    internal void ReopenReview()
    {
        Status = DocumentStatus.InReview;
        ReviewReason = null;
        ReviewedByName = null;
        ReviewedAt = null;
    }
}

/// <summary>
/// A document of a process: a requested one (a field the customer fills with a file), an extra one or a deliverable.
/// Every upload is a new version; the latest version decides the status.
/// </summary>
public sealed class ProcessDocument
{
    public const int MaxNameLength = 200;

    private readonly List<DocumentVersion> _versions = [];

    public Guid Id { get; private set; }
    public Guid ProcessId { get; private set; }

    /// <summary>The step it belongs to; null for extras, which belong to the whole process.</summary>
    public Guid? StepId { get; private set; }

    public DocumentKind Kind { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; }

    /// <summary>Order inside the step, as configured in the type.</summary>
    public int Position { get; private set; }

    /// <summary>Kept in line with the latest version, so lists can count what is pending without loading versions.</summary>
    public DocumentStatus Status { get; private set; }

    /// <summary>Changes on every change of the document: saving over a version someone else already changed is refused (409).</summary>
    public Guid Version { get; private set; }

    public IReadOnlyList<DocumentVersion> Versions => _versions.OrderBy(v => v.Number).ToList();
    public DocumentVersion? LatestVersion => _versions.MaxBy(v => v.Number);

    /// <summary>Required and not approved yet: advancing the step now is the analyst's call, and it is recorded.</summary>
    public bool IsPending => Kind == DocumentKind.Requested && IsRequired && Status is not (DocumentStatus.Approved or DocumentStatus.Waived);

    private ProcessDocument() { }

    public static ProcessDocument Requested(Guid processId, Guid stepId, string name, bool isRequired, int position) =>
        New(processId, stepId, DocumentKind.Requested, name, isRequired, position);

    public static ProcessDocument Deliverable(Guid processId, Guid stepId, string name, int position) =>
        New(processId, stepId, DocumentKind.Deliverable, name, false, position);

    /// <summary>A document nobody asked for, described by whoever sends it; the file comes right after with <see cref="Upload"/>.</summary>
    public static ProcessDocument Extra(Guid processId, string? description, Actor actor, DateTimeOffset now)
    {
        var name = description?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            throw new ArgumentException($"A description of up to {MaxNameLength} characters is required.", nameof(description));
        return New(processId, null, DocumentKind.Extra, name, false, 0);
    }

    /// <summary>Adds a version: deliverables are published right away, the others wait for review.</summary>
    public DocumentVersion Upload(StoredFile file, Actor actor, DateTimeOffset now)
    {
        var status = Kind == DocumentKind.Deliverable ? DocumentStatus.Published : DocumentStatus.InReview;
        var version = DocumentVersion.Of(_versions.Count + 1, file, actor, now, status);
        _versions.Add(version);
        Status = status;
        Version = Guid.NewGuid();
        return version;
    }

    public void Approve(Actor actor, DateTimeOffset now)
    {
        RequireInReview().Review(Status = DocumentStatus.Approved, actor, null, now);
        Version = Guid.NewGuid();
    }

    public void Reject(Actor actor, string? reason, DateTimeOffset now)
    {
        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (text is null || text.Length > Process.MaxCommentLength)
            throw new ArgumentException("A reason is required to reject a document.", nameof(reason));

        RequireInReview().Review(Status = DocumentStatus.Rejected, actor, text, now);
        Version = Guid.NewGuid();
    }

    /// <summary>The staff send an approved document back to review (only they can: the customer never reopens it).</summary>
    public void Reopen()
    {
        if (Status != DocumentStatus.Approved)
            throw new ProcessRuleException("Only approved documents can be reopened.");

        LatestVersion!.ReopenReview();
        Status = DocumentStatus.InReview;
        Version = Guid.NewGuid();
    }

    /// <summary>
    /// Its step ended: a requested document still waiting for the customer (never sent or rejected) is not waited for
    /// anymore. Documents in review or approved are kept as they are. True when it changed.
    /// </summary>
    public bool Waive()
    {
        if (Kind != DocumentKind.Requested || Status is not (DocumentStatus.AwaitingUpload or DocumentStatus.Rejected))
            return false;

        Status = DocumentStatus.Waived;
        Version = Guid.NewGuid();
        return true;
    }

    /// <summary>Its step started again: a waived document is waited for again, as it was before (never sent or rejected).</summary>
    public bool Restore()
    {
        if (Status != DocumentStatus.Waived)
            return false;

        Status = LatestVersion?.Status == DocumentStatus.Rejected ? DocumentStatus.Rejected : DocumentStatus.AwaitingUpload;
        Version = Guid.NewGuid();
        return true;
    }

    private DocumentVersion RequireInReview()
    {
        if (Kind == DocumentKind.Deliverable)
            throw new ProcessRuleException("Deliverables are not reviewed.");
        if (Status != DocumentStatus.InReview)
            throw new ProcessRuleException("Only a document waiting for review can be approved or rejected.");
        return LatestVersion!;
    }

    private static ProcessDocument New(Guid processId, Guid? stepId, DocumentKind kind, string name, bool isRequired, int position) => new()
    {
        Id = Ids.New(),
        ProcessId = processId,
        StepId = stepId,
        Kind = kind,
        Name = name.Trim(),
        IsRequired = isRequired,
        Position = position,
        Status = DocumentStatus.AwaitingUpload,
        Version = Guid.NewGuid(),
    };
}
