namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>Finds active steps past their deadline and tells the people tied to each process (once per step run).</summary>
public interface IOverdueStepService
{
    /// <summary>How many processes got a new overdue warning.</summary>
    Task<int> NotifyOverdueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
