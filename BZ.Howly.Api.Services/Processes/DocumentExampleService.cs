using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Interfaces.Storage;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Processes;

public sealed class DocumentExampleService(
    IDocumentExampleRepository repository,
    IFileStorage fileStorage,
    TimeProvider timeProvider,
    ILogger<DocumentExampleService> logger) : IDocumentExampleService
{
    private const string Folder = "examples";

    public async Task<OperationResult<DocumentExampleResponse>> UploadAsync(IncomingFile file, Guid actorId, CancellationToken cancellationToken = default)
    {
        var stored = await FileIntake.StoreAsync(fileStorage, Folder, file, logger, cancellationToken);
        if (stored.Errors is not null)
            return OperationResult<DocumentExampleResponse>.Invalid(stored.Errors);

        // Saved on its own, before the type: an example the type never uses is a harmless orphan.
        var example = DocumentExample.Of(stored.File!, actorId, timeProvider.GetUtcNow());
        await repository.AddAsync(example, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return OperationResult<DocumentExampleResponse>.Success(new DocumentExampleResponse(example.Id, example.FileName, example.Size));
    }

    public async Task<DocumentFile?> OpenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var example = await repository.FindAsync(id, cancellationToken);
        var stream = example is null ? null : await fileStorage.OpenReadAsync(example.FileKey, cancellationToken);
        return stream is null ? null : new DocumentFile(stream, example!.FileName);
    }
}
