using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>An uploaded file as received: the original name and its content.</summary>
public sealed record IncomingFile(string? FileName, Stream Content);

/// <summary>A stored document version ready to download.</summary>
public sealed record DocumentFile(Stream Content, string FileName);

/// <summary>
/// Documents of processes. <paramref name="customerUserId"/> (on the methods that take it) means the call comes from that
/// customer: only their processes, only requested/extra documents. Null means the staff.
/// Every write answers with the updated process.
/// </summary>
public interface IProcessDocumentService
{
    Task<OperationResult<ProcessResponse>> UploadAsync(
        Guid processId, Guid documentId, IncomingFile file, Guid actorId, Guid? customerUserId, CancellationToken cancellationToken = default);

    /// <summary>A document nobody asked for: customers add extras; the staff add extras or deliverables.</summary>
    Task<OperationResult<ProcessResponse>> AddAsync(
        Guid processId, DocumentKind kind, string? name, IncomingFile file, Guid actorId, Guid? customerUserId, CancellationToken cancellationToken = default);

    Task<OperationResult<ProcessResponse>> ApproveAsync(Guid processId, Guid documentId, Guid actorId, CancellationToken cancellationToken = default);
    Task<OperationResult<ProcessResponse>> RejectAsync(Guid processId, Guid documentId, string? reason, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>Sends an approved document back to review (staff only).</summary>
    Task<OperationResult<ProcessResponse>> ReopenAsync(Guid processId, Guid documentId, Guid actorId, CancellationToken cancellationToken = default);

    Task<DocumentFile?> OpenAsync(Guid processId, Guid documentId, Guid versionId, Guid? customerUserId, CancellationToken cancellationToken = default);

    /// <summary>The example file of a requested document; null when it has none.</summary>
    Task<DocumentFile?> OpenExampleAsync(Guid processId, Guid documentId, Guid? customerUserId, CancellationToken cancellationToken = default);
}
