using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class OnboardingRepository(HowlyDbContext context) : IOnboardingRepository
{
    public async Task<OnboardingResponse> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // The tenant table is not filtered by tenant: it is read by id.
        var profile = await context.Tenants.Where(t => t.Id == tenantId).Select(t => new { t.Profile.LegalName, t.Profile.Cnpj, t.Profile.Email }).SingleAsync(cancellationToken);
        var internalRoles = Roles.Internal.Select(r => r.ToUpperInvariant()).ToList();
        var staff = await (
                from user in context.Users
                where user.IsActive && context.UserRoles.Any(ur => ur.UserId == user.Id &&
                    context.Roles.Any(r => r.Id == ur.RoleId && internalRoles.Contains(r.NormalizedName!)))
                select user.Id)
            .CountAsync(cancellationToken);

        return new OnboardingResponse(
            CompanyProfile: profile.LegalName is not null && profile.Cnpj is not null && profile.Email is not null,
            Agencies: await context.Agencies.AnyAsync(a => a.IsActive, cancellationToken),
            ProcessTypes: await context.ProcessTypes.AnyAsync(t => t.IsActive, cancellationToken),
            Customers: await context.Customers.AnyAsync(cancellationToken),
            Processes: await context.Processes.AnyAsync(cancellationToken),
            // Someone besides whoever set the consultancy up.
            Team: staff > 1);
    }
}
