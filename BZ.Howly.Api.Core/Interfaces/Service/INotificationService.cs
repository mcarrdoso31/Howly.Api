using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>
/// The single place that tells people about process changes. Today: the portal (the bell). Other channels (email)
/// plug in here later, without touching the use cases.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Creates the notifications of the events the process got since <paramref name="eventsBefore"/>, for its customer and
    /// its assignee except <paramref name="actorId"/>. Saved together with the process by the caller.
    /// </summary>
    Task NotifyAsync(Process process, int eventsBefore, Guid actorId, CancellationToken cancellationToken = default) =>
        NotifyAsync(process, eventsBefore, actorId, null, cancellationToken);

    /// <summary>
    /// Same, also telling <paramref name="previousAssigneeId"/> (the process just left their hands, so they are not its
    /// assignee anymore).
    /// </summary>
    Task NotifyAsync(Process process, int eventsBefore, Guid actorId, Guid? previousAssigneeId, CancellationToken cancellationToken = default);

    Task<NotificationPageResponse> ListAsync(Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>False when the notification does not exist or is someone else's.</summary>
    Task<bool> MarkReadAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
