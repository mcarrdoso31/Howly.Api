using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>The signed-in person's notifications (the bell), for staff and customers alike.</summary>
[ApiController]
[Route("api/me/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<NotificationPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await notificationService.ListAsync(this.CurrentUserId(), unreadOnly, page, pageSize, cancellationToken));

    /// <summary>Records the reading; reading again keeps the first time.</summary>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        await notificationService.MarkReadAsync(this.CurrentUserId(), id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await notificationService.MarkAllReadAsync(this.CurrentUserId(), cancellationToken);
        return NoContent();
    }
}
