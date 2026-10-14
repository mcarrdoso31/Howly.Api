using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IProcessDocumentRepository
{
    Task AddAsync(ProcessDocument document, CancellationToken cancellationToken = default);

    /// <summary>With versions, ordered by step order then position (extras last).</summary>
    Task<IReadOnlyList<ProcessDocument>> ListByProcessAsync(Guid processId, CancellationToken cancellationToken = default);

    Task<ProcessDocument?> FindAsync(Guid processId, Guid documentId, CancellationToken cancellationToken = default);
}
