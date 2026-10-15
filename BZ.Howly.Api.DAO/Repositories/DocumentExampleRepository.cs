using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class DocumentExampleRepository(HowlyDbContext context) : IDocumentExampleRepository
{
    public async Task AddAsync(DocumentExample example, CancellationToken cancellationToken = default) =>
        await context.DocumentExamples.AddAsync(example, cancellationToken);

    public Task<DocumentExample?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.DocumentExamples.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DocumentExample>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        ids.Count == 0 ? [] : await context.DocumentExamples.Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
