using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class CustomerRepository(HowlyDbContext context) : ICustomerRepository
{
    public async Task AddAsync(Customer customer, CancellationToken cancellationToken = default) =>
        await context.Customers.AddAsync(customer, cancellationToken);

    public Task<Customer?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Customer?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Customers.SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public Task<bool> ExistsAsync(TaxId document, CancellationToken cancellationToken = default) =>
        context.Customers.AnyAsync(c => c.Document == document, cancellationToken);

    public async Task<(IReadOnlyList<CustomerListItem> Items, int TotalCount)> SearchAsync(
        string? search, CustomerStatusFilter status, int skip, int take, CancellationToken cancellationToken = default)
    {
        // The customer's login username is the normalized CPF/CNPJ, which keeps the document searchable as plain text.
        var query =
            from customer in context.Customers
            join user in context.Users on customer.UserId equals user.Id
            select new { customer, user };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            var documentTerm = new string(term.Where(char.IsAsciiLetterOrDigit).ToArray());

            // ToUpper on both sides keeps the search case-insensitive on SQL Server, SQLite and PostgreSQL alike.
            query = query.Where(x =>
                x.customer.CompanyName.ToUpper().Contains(term) ||
                (documentTerm.Length > 0 && x.user.UserName!.Contains(documentTerm)));
        }

        query = status switch
        {
            CustomerStatusFilter.Active => query.Where(x => x.user.IsActive),
            CustomerStatusFilter.Inactive => query.Where(x => !x.user.IsActive),
            _ => query,
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.customer.CompanyName)
            .ThenBy(x => x.customer.Id)
            .Skip(skip)
            .Take(take)
            .Select(x => new CustomerListItem(
                x.customer.Id, x.user.UserName!, x.customer.CompanyName, x.user.IsActive, x.user.MustChangePassword, x.customer.ConfirmedAt))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
