using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Processes;

/// <summary>
/// A file the staff attach to a requested document as an example to follow (a letter template, a filled-in form…).
/// Never changed nor deleted: process types and the processes opened from them point to it.
/// </summary>
public sealed class DocumentExample
{
    public Guid Id { get; private set; }
    public string FileKey { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long Size { get; private set; }
    public Guid UploadedById { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private DocumentExample() { }

    public static DocumentExample Of(StoredFile file, Guid uploadedById, DateTimeOffset now) => new()
    {
        Id = Ids.New(),
        FileKey = file.Key,
        FileName = file.FileName,
        Size = file.Size,
        UploadedById = uploadedById,
        UploadedAt = now,
    };
}
