using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IDocumentExampleRepository
{
    Task AddAsync(DocumentExample example, CancellationToken cancellationToken = default);
    Task<DocumentExample?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentExample>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
