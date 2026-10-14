using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class UserPreferencesRepository(HowlyDbContext context) : IUserPreferencesRepository
{
    public Task<UserPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.UserPreferences.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public async Task AddAsync(UserPreferences preferences, CancellationToken cancellationToken = default) =>
        await context.UserPreferences.AddAsync(preferences, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
