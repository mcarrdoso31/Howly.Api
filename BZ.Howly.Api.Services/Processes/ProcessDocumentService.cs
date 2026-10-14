using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Interfaces.Storage;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Core.Shared.Exceptions;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Processes;

public sealed class ProcessDocumentService(
    IProcessRepository processRepository,
    IProcessDocumentRepository documentRepository,
    ICustomerRepository customerRepository,
    IUserAccountStore userAccountStore,
    IFileStorage fileStorage,
    IProcessService processService,
    INotificationService notificationService,
    TimeProvider timeProvider,
    ILogger<ProcessDocumentService> logger) : IProcessDocumentService
{
    private const string Folder = "documents";

    /// <summary>Files keep a neutral extension on disk: the original name lives in the database and comes back on download.</summary>
    private const string StoredExtension = ".bin";

    public async Task<OperationResult<ProcessResponse>> UploadAsync(
        Guid processId, Guid documentId, IncomingFile file, Guid actorId, Guid? customerUserId, CancellationToken cancellationToken = default)
    {
        var process = await FindProcessAsync(processId, customerUserId, cancellationToken);
        var document = process is null ? null : await documentRepository.FindAsync(processId, documentId, cancellationToken);
        if (process is null || document is null)
            return OperationResult<ProcessResponse>.NotFound();
        if (RefusedToSend(process, customerUserId is null ? null : document, customerUserId is not null) is { } refused)
            return refused;

        var stored = await StoreAsync(file, cancellationToken);
        if (stored.Errors is not null)
            return OperationResult<ProcessResponse>.Invalid(stored.Errors);

        await SaveOrDiscardAsync(stored.File!, async () =>
        {
            var actor = await ActorAsync(actorId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var eventsBefore = process.Events.Count;
            document.Upload(stored.File!, actor, now);
            process.RecordDocumentEvent(document.Kind == DocumentKind.Deliverable ? ProcessEventType.DeliverablePublished : ProcessEventType.DocumentUploaded,
                document.Name, actor, now);
            await notificationService.NotifyAsync(process, eventsBefore, actorId, cancellationToken);
            await processRepository.SaveChangesAsync(cancellationToken);
        });

        return await ResponseAsync(processId, customerUserId, cancellationToken);
    }

    public async Task<OperationResult<ProcessResponse>> AddAsync(
        Guid processId, DocumentKind kind, string? name, IncomingFile file, Guid actorId, Guid? customerUserId, CancellationToken cancellationToken = default)
    {
        var process = await FindProcessAsync(processId, customerUserId, cancellationToken);
        if (process is null)
            return OperationResult<ProcessResponse>.NotFound();
        if (kind == DocumentKind.Requested || (customerUserId is not null && kind != DocumentKind.Extra))
            return OperationResult<ProcessResponse>.Invalid("kind", "Customers add extra documents; the staff add extras or deliverables.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > ProcessDocument.MaxNameLength)
            return OperationResult<ProcessResponse>.Invalid("name", $"A description of up to {ProcessDocument.MaxNameLength} characters is required.");
        if (RefusedToSend(process, null, customerUserId is not null) is { } refused)
            return refused;

        var stored = await StoreAsync(file, cancellationToken);
        if (stored.Errors is not null)
            return OperationResult<ProcessResponse>.Invalid(stored.Errors);

        await SaveOrDiscardAsync(stored.File!, async () =>
        {
            var actor = await ActorAsync(actorId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            // Staff deliverables added by hand belong to the step they are delivered in.
            var document = kind == DocumentKind.Deliverable
                ? ProcessDocument.Deliverable(processId, process.ActiveStep?.Id ?? process.Steps[^1].Id, name.Trim(), int.MaxValue)
                : ProcessDocument.Extra(processId, name, actor, now);
            document.Upload(stored.File!, actor, now);
            await documentRepository.AddAsync(document, cancellationToken);
            var eventsBefore = process.Events.Count;
            process.RecordDocumentEvent(kind == DocumentKind.Deliverable ? ProcessEventType.DeliverablePublished : ProcessEventType.DocumentUploaded,
                document.Name, actor, now);
            await notificationService.NotifyAsync(process, eventsBefore, actorId, cancellationToken);
            await processRepository.SaveChangesAsync(cancellationToken);
        });

        return await ResponseAsync(processId, customerUserId, cancellationToken);
    }

    public Task<OperationResult<ProcessResponse>> ApproveAsync(Guid processId, Guid documentId, Guid actorId, CancellationToken cancellationToken = default) =>
        ReviewAsync(processId, documentId, actorId, ProcessEventType.DocumentApproved, null, (document, actor, now) => document.Approve(actor, now), cancellationToken);

    public Task<OperationResult<ProcessResponse>> RejectAsync(
        Guid processId, Guid documentId, string? reason, Guid actorId, CancellationToken cancellationToken = default) =>
        ReviewAsync(processId, documentId, actorId, ProcessEventType.DocumentRejected, reason, (document, actor, now) => document.Reject(actor, reason, now), cancellationToken);

    /// <summary>Only the staff send an approved document back to review.</summary>
    public Task<OperationResult<ProcessResponse>> ReopenAsync(Guid processId, Guid documentId, Guid actorId, CancellationToken cancellationToken = default) =>
        ReviewAsync(processId, documentId, actorId, ProcessEventType.DocumentReopened, null, (document, _, _) => document.Reopen(), cancellationToken);

    public async Task<DocumentFile?> OpenAsync(Guid processId, Guid documentId, Guid versionId, Guid? customerUserId, CancellationToken cancellationToken = default)
    {
        var process = await FindProcessAsync(processId, customerUserId, cancellationToken);
        var document = process is null ? null : await documentRepository.FindAsync(processId, documentId, cancellationToken);
        var version = document?.Versions.SingleOrDefault(v => v.Id == versionId);
        if (version is null)
            return null;

        var stream = await fileStorage.OpenReadAsync(version.FileKey, cancellationToken);
        return stream is null ? null : new DocumentFile(stream, version.FileName);
    }

    private async Task<OperationResult<ProcessResponse>> ReviewAsync(
        Guid processId, Guid documentId, Guid actorId, ProcessEventType eventType, string? reason,
        Action<ProcessDocument, Actor, DateTimeOffset> review, CancellationToken cancellationToken)
    {
        var process = await processRepository.FindByIdAsync(processId, cancellationToken);
        var document = process is null ? null : await documentRepository.FindAsync(processId, documentId, cancellationToken);
        if (process is null || document is null)
            return OperationResult<ProcessResponse>.NotFound();

        var actor = await ActorAsync(actorId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        try
        {
            process.RequireOpenForDocuments();
            review(document, actor, now);
        }
        catch (ProcessRuleException exception)
        {
            return OperationResult<ProcessResponse>.Conflict(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return OperationResult<ProcessResponse>.Invalid("reason", exception.Message);
        }

        var eventsBefore = process.Events.Count;
        process.RecordDocumentEvent(eventType, document.Name, actor, now, reason);
        await notificationService.NotifyAsync(process, eventsBefore, actorId, cancellationToken);
        await processRepository.SaveChangesAsync(cancellationToken);
        return await ResponseAsync(processId, null, cancellationToken);
    }

    /// <summary>Checks and stores the file; on a refusal the errors point to the "file" field.</summary>
    private async Task<(StoredFile? File, Dictionary<string, string[]>? Errors)> StoreAsync(IncomingFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await file.Content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > UploadPolicy.MaxBytes)
                return (null, FileError("The file must have at most 25 MB."));
            buffer.Write(chunk, 0, read);
        }

        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, UploadPolicy.HeaderLength));
        var check = UploadPolicy.Check(file.FileName, header);
        if (check != UploadCheck.Accepted)
        {
            if (check is UploadCheck.BlockedType or UploadCheck.BlockedContent)
                logger.LogWarning("Refused upload {FileName}: {Check}.", UploadPolicy.CleanFileName(file.FileName), check);
            return (null, FileError(check switch
            {
                UploadCheck.BlockedType or UploadCheck.BlockedContent => "Executables, scripts and web pages are not accepted.",
                UploadCheck.Empty => "The file is empty.",
                _ => "The file needs a name with an extension.",
            }));
        }

        buffer.Position = 0;
        var key = await fileStorage.SaveAsync(Folder, StoredExtension, buffer, cancellationToken);
        return (new StoredFile(key, UploadPolicy.CleanFileName(file.FileName), buffer.Length), null);
    }

    private static Dictionary<string, string[]> FileError(string message) => new() { ["file"] = [message] };

    /// <summary>
    /// The database part of an upload. When it surely did not save (a rule, a conflict, invalid data), the file just
    /// stored is removed. When the outcome is unknown (the connection dropped, maybe after the commit), the file is kept:
    /// an orphan file is harmless, a saved version pointing to a deleted file is a lost document.
    /// </summary>
    private async Task SaveOrDiscardAsync(StoredFile file, Func<Task> save)
    {
        try
        {
            await save();
        }
        catch (Exception exception) when (exception is HowlyException or ArgumentException)
        {
            try
            {
                await fileStorage.DeleteAsync(file.Key, CancellationToken.None);
            }
            catch (Exception cleanup)
            {
                logger.LogWarning(cleanup, "Could not remove the file of a failed upload.");
            }

            throw;
        }
    }

    /// <summary>
    /// Checked before the file is stored: the staff send while the process is in progress; the customer only on their
    /// own step (<paramref name="document"/> null for a new extra). Null when allowed.
    /// </summary>
    private static OperationResult<ProcessResponse>? RefusedToSend(Process process, ProcessDocument? document, bool byCustomer)
    {
        try
        {
            if (byCustomer)
                process.RequireCustomerCanSend(document);
            else
                process.RequireOpenForDocuments();
            return null;
        }
        catch (ProcessRuleException exception)
        {
            return OperationResult<ProcessResponse>.Conflict(exception.Message);
        }
    }

    /// <summary>The process, as long as the caller may see it (a customer only their own).</summary>
    private async Task<Process?> FindProcessAsync(Guid processId, Guid? customerUserId, CancellationToken cancellationToken)
    {
        var process = await processRepository.FindByIdAsync(processId, cancellationToken);
        if (process is null || customerUserId is null)
            return process;

        var customer = await customerRepository.FindByUserIdAsync(customerUserId.Value, cancellationToken);
        return customer is not null && process.CustomerId == customer.Id ? process : null;
    }

    private Task<OperationResult<ProcessResponse>> ResponseAsync(Guid processId, Guid? customerUserId, CancellationToken cancellationToken) =>
        customerUserId is { } userId ? processService.GetForCustomerAsync(userId, processId, cancellationToken) : processService.GetAsync(processId, cancellationToken);

    private async Task<Actor> ActorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userAccountStore.FindByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException("The signed-in user no longer exists.");
        return new Actor(user.Id, user.DisplayName);
    }
}
