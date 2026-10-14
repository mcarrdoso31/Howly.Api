namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface IUnitOfWork
{
    /// <summary>Runs <paramref name="work"/> in a database transaction, committing only when it completes without throwing.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
}
