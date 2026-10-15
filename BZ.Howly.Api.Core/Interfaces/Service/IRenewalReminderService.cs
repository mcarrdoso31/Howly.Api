namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>Reminds the staff when the renewal of a license should start (they open it).</summary>
public interface IRenewalReminderService
{
    /// <summary>Reminds about every license of the current consultancy due by its local date at <paramref name="now"/>; returns how many.</summary>
    Task<int> NotifyDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
