using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>Example files of requested documents, uploaded by the staff while configuring a process type.</summary>
public interface IDocumentExampleService
{
    /// <summary>Checked like any document upload; the id then goes in the type's document ("exampleId").</summary>
    Task<OperationResult<DocumentExampleResponse>> UploadAsync(IncomingFile file, Guid actorId, CancellationToken cancellationToken = default);

    Task<DocumentFile?> OpenAsync(Guid id, CancellationToken cancellationToken = default);
}
