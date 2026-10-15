namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>Reminds the staff when a paused process reaches its expected return (they decide whether to resume).</summary>
public interface IPauseReturnService
{
    /// <summary>Reminds about every paused process of the current consultancy due by its local date at <paramref name="now"/>; returns how many.</summary>
    Task<int> NotifyDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
