namespace BZ.Howly.Api.Core.Interfaces.DAO;

/// <summary>
/// Coordinates a periodic job across every instance of the API: one round at a time, and (with a minimum gap) one
/// round per interval, however many instances tick their own timers.
/// </summary>
public interface IJobLock
{
    /// <summary>
    /// Starts a round: the held lock (dispose it when the round ends), or null right away when another instance is
    /// running one, or when the last round started less than <paramref name="minimumGap"/> before
    /// <paramref name="now"/>. Without a gap every free moment is a round (manual runs, tests).
    /// </summary>
    Task<IAsyncDisposable?> TryStartRoundAsync(string name, DateTimeOffset now, TimeSpan? minimumGap = null, CancellationToken cancellationToken = default);
}
