using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BZ.Howly.Api.Controllers;

/// <summary>Public face of an account to other signed-in users (for now, the photo or logo).</summary>
[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(IAccountProfileService accountProfileService) : ControllerBase
{
    /// <summary>Clients add ?v={avatarVersion}: each version is cached for a day, a new image gets a new URL.</summary>
    [HttpGet("{id:guid}/avatar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvatar(Guid id, CancellationToken cancellationToken)
    {
        var avatar = await accountProfileService.OpenAvatarAsync(this.CurrentUserId(), id, cancellationToken);
        if (avatar is null)
            return NotFound();

        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        Response.Headers[HeaderNames.CacheControl] = "private, max-age=86400";
        return File(avatar.Content, avatar.ContentType);
    }
}
