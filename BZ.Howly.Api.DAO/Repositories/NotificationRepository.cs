using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Notifications;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

public sealed class NotificationRepository(HowlyDbContext context) : INotificationRepository
{
    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default) =>
        await context.Notifications.AddAsync(notification, cancellationToken);

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount)> ListAsync(
        Guid userId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Notifications.Where(n => n.UserId == userId && (!unreadOnly || n.ReadAt == null));
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.EventSequence)
            .ThenByDescending(n => n.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);

    public Task<Notification?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
        context.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    // One UPDATE, inside the tenant (the query filter applies to bulk updates too).
    public Task MarkAllReadAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        context.Notifications.Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ReadAtByEventAsync(Guid processId, Guid userId, CancellationToken cancellationToken = default) =>
        await context.Notifications
            .Where(n => n.ProcessId == processId && n.UserId == userId && n.ReadAt != null)
            .ToDictionaryAsync(n => n.EventId, n => n.ReadAt!.Value, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
