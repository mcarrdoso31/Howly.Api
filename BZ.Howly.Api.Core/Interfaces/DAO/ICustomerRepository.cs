using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface ICustomerRepository
{
    Task AddAsync(Customer customer, CancellationToken cancellationToken = default);
    Task<Customer?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Customer?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(TaxId document, CancellationToken cancellationToken = default);

    /// <summary>Filters by company name or CNPJ (both partial, case-insensitive) and by status, ordered by company name.</summary>
    Task<(IReadOnlyList<CustomerListItem> Items, int TotalCount)> SearchAsync(
        string? search, CustomerStatusFilter status, int skip, int take, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
