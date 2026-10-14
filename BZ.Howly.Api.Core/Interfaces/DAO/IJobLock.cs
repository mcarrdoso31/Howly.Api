namespace BZ.Howly.Api.Core.Interfaces.DAO;

/// <summary>
/// A lock shared by every instance of the API, so a periodic job runs once per round instead of once per instance.
/// </summary>
public interface IJobLock
{
    /// <summary>The held lock (dispose to release it), or null right away when another instance holds it.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken = default);
}
