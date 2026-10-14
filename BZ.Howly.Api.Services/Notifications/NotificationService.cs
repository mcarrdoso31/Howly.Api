using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Notifications;
using BZ.Howly.Api.Core.Processes;

namespace BZ.Howly.Api.Services.Notifications;

public sealed class NotificationService(
    INotificationRepository notificationRepository,
    ICustomerRepository customerRepository,
    TimeProvider timeProvider) : INotificationService
{
    public const int MaxPageSize = 50;

    public Task NotifyAsync(Process process, int eventsBefore, Guid actorId, CancellationToken cancellationToken = default) =>
        NotifyAsync(process, eventsBefore, actorId, null, cancellationToken);

    public async Task NotifyAsync(Process process, int eventsBefore, Guid actorId, Guid? previousAssigneeId, CancellationToken cancellationToken = default)
    {
        var newEvents = process.Events.Skip(eventsBefore).ToList();
        if (newEvents.Count == 0)
            return;

        var customer = await customerRepository.FindByIdAsync(process.CustomerId, cancellationToken);
        var recipients = Notification.Recipients(customer?.UserId, process.AssigneeId, actorId, previousAssigneeId);

        foreach (var processEvent in newEvents)
            foreach (var userId in recipients.Where(id => processEvent.IsVisibleToCustomer || id != customer?.UserId))
                await notificationRepository.AddAsync(Notification.For(userId, process, processEvent), cancellationToken);
    }

    public async Task<NotificationPageResponse> ListAsync(Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var (items, total) = await notificationRepository.ListAsync(userId, unreadOnly, (page - 1) * pageSize, pageSize, cancellationToken);
        var unread = await notificationRepository.CountUnreadAsync(userId, cancellationToken);

        return new NotificationPageResponse(
            items.Select(n => new NotificationResponse(n.Id, n.ProcessId, n.ProcessTitle, n.Type, n.StepName, n.Details, n.ActorName, n.CreatedAt, n.ReadAt)).ToList(),
            unread, page, pageSize, total);
    }

    public async Task<bool> MarkReadAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var notification = await notificationRepository.FindAsync(userId, id, cancellationToken);
        if (notification is null)
            return false;

        notification.MarkRead(timeProvider.GetUtcNow());
        await notificationRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var notification in await notificationRepository.ListUnreadAsync(userId, cancellationToken))
            notification.MarkRead(now);
        await notificationRepository.SaveChangesAsync(cancellationToken);
    }
}
