using BZ.Howly.Api.Core.Identity;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IUserPreferencesRepository
{
    Task<UserPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
