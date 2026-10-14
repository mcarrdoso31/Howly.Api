using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class ProcessTypeRepository(HowlyDbContext context) : IProcessTypeRepository
{
    public async Task AddAsync(ProcessType type, CancellationToken cancellationToken = default) =>
        await context.ProcessTypes.AddAsync(type, cancellationToken);

    public Task<ProcessType?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.ProcessTypes.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProcessTypeListItemResponse>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        await context.ProcessTypes
            .Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new ProcessTypeListItemResponse(t.Id, t.Name, t.Description, t.IsActive, t.Steps.Count))
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
