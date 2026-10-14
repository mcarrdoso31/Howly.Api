using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.DAO.Context;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class UnitOfWork(HowlyDbContext context) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var result = await work(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
