using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Tenancy;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class TenantRepository(HowlyDbContext context) : ITenantRepository
{
    public Task<Tenant?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        context.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken);

    public async Task<IReadOnlyList<Tenant>> ListActiveConsultanciesAsync(CancellationToken cancellationToken = default) =>
        await context.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active && !t.IsPlatform)
            .OrderBy(t => t.Slug)
            .ToListAsync(cancellationToken);
}
