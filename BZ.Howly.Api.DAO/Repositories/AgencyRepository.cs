using BZ.Howly.Api.Core.Agencies;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class AgencyRepository(HowlyDbContext context) : IAgencyRepository
{
    public async Task AddAsync(Agency agency, CancellationToken cancellationToken = default) =>
        await context.Agencies.AddAsync(agency, cancellationToken);

    public Task<Agency?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Agencies.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Agency>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        await context.Agencies
            .Where(a => includeInactive || a.IsActive)
            .OrderBy(a => a.Acronym).ThenBy(a => a.Name).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Agency>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        ids.Count == 0 ? [] : await context.Agencies.Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
