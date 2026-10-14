using BZ.Howly.Api.Core.Tenancy;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

/// <summary>The consultancies themselves (not scoped by tenant).</summary>
public interface ITenantRepository
{
    Task<Tenant?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Active consultancies, the platform portal left out (e.g. for jobs that go through each one).</summary>
    Task<IReadOnlyList<Tenant>> ListActiveConsultanciesAsync(CancellationToken cancellationToken = default);
}
