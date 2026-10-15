using BZ.Howly.Api.Core.Notifications;

namespace BZ.Howly.Api.Core.Interfaces.DAO;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<(IReadOnlyList<Notification> Items, int TotalCount)> ListAsync(
        Guid userId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Null when it does not exist or belongs to someone else.</summary>
    Task<Notification?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Marks every unread notification of <paramref name="userId"/> as read, in one statement (it saves by itself).</summary>
    Task MarkAllReadAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>When <paramref name="userId"/> read the notification of each event of the process (read ones only).</summary>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ReadAtByEventAsync(Guid processId, Guid userId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
