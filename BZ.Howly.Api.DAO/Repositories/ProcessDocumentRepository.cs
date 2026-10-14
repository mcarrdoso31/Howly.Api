using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class ProcessDocumentRepository(HowlyDbContext context) : IProcessDocumentRepository
{
    public async Task AddAsync(ProcessDocument document, CancellationToken cancellationToken = default) =>
        await context.ProcessDocuments.AddAsync(document, cancellationToken);

    public async Task<IReadOnlyList<ProcessDocument>> ListByProcessAsync(Guid processId, CancellationToken cancellationToken = default)
    {
        var documents = await context.ProcessDocuments.Where(d => d.ProcessId == processId).ToListAsync(cancellationToken);
        // Steps order lives on the process; sort in memory: by the step position, then the document position, extras last.
        var stepOrder = await context.Processes.Where(p => p.Id == processId)
            .SelectMany(p => p.Steps.Select(s => new { s.Id, s.Position }))
            .ToDictionaryAsync(s => s.Id, s => s.Position, cancellationToken);

        return documents
            .OrderBy(d => d.StepId is { } stepId && stepOrder.TryGetValue(stepId, out var position) ? position : int.MaxValue)
            .ThenBy(d => d.Kind == DocumentKind.Deliverable)
            .ThenBy(d => d.Position)
            .ThenBy(d => d.Name)
            .ToList();
    }

    public Task<ProcessDocument?> FindAsync(Guid processId, Guid documentId, CancellationToken cancellationToken = default) =>
        context.ProcessDocuments.SingleOrDefaultAsync(d => d.ProcessId == processId && d.Id == documentId, cancellationToken);
}
