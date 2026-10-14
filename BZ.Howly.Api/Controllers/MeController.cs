using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Extensions;
using BZ.Howly.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>Resources that belong to the signed-in user, whatever the profile.</summary>
[ApiController]
[Route("api/me")]
[AllowOnPlatform]
[Authorize]
[AllowPendingPasswordChange]
[AllowPendingProfileConfirmation]
public sealed class MeController(IPreferencesService preferencesService, IAccountProfileService accountProfileService) : ControllerBase
{
    [HttpGet("preferences")]
    [ProducesResponseType<PreferencesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
    {
        var preferences = await preferencesService.GetAsync(this.CurrentUserId(), cancellationToken);
        return preferences is null ? NoContent() : Ok(preferences);
    }

    [HttpGet("profile")]
    [ProducesResponseType<AccountProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken) =>
        this.ToActionResult(await accountProfileService.GetAsync(this.CurrentUserId(), cancellationToken), Ok);

    /// <summary>Staff only: customers edit their company (trade name, contacts) at api/me/company.</summary>
    [HttpPut("profile")]
    [Authorize(Policy = Policies.Internal)]
    [ProducesResponseType<AccountProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile(UpdateStaffProfileRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await accountProfileService.UpdateStaffAsync(this.CurrentUserId(), request, cancellationToken), Ok);

    /// <summary>Photo (staff) or company logo (customers). The size limit leaves room for the multipart envelope.</summary>
    [HttpPut("avatar")]
    [RequestSizeLimit(IAccountProfileService.MaxAvatarBytes + 64 * 1024)]
    [ProducesResponseType<AccountProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["Send the image in the \"file\" field."] }));

        await using var content = file.OpenReadStream();
        return this.ToActionResult(await accountProfileService.SetAvatarAsync(this.CurrentUserId(), content, cancellationToken), Ok);
    }

    [HttpDelete("avatar")]
    [ProducesResponseType<AccountProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveAvatar(CancellationToken cancellationToken) =>
        this.ToActionResult(await accountProfileService.RemoveAvatarAsync(this.CurrentUserId(), cancellationToken), Ok);

    [HttpPut("preferences")]
    [ProducesResponseType<PreferencesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SavePreferences(UpdatePreferencesRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await preferencesService.SaveAsync(this.CurrentUserId(), request, cancellationToken), Ok);
}
