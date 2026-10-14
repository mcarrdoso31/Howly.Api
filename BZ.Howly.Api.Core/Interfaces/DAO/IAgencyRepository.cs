using BZ.Howly.Api.Core.Agencies;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IAgencyRepository
{
    Task AddAsync(Agency agency, CancellationToken cancellationToken = default);
    Task<Agency?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>By acronym, then name.</summary>
    Task<IReadOnlyList<Agency>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>The agencies among <paramref name="ids"/> that exist (in the current consultancy).</summary>
    Task<IReadOnlyList<Agency>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
