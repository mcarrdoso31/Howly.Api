using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Tenancy;
using BZ.Howly.Api.Extensions;
using BZ.Howly.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BZ.Howly.Api.Controllers;

/// <summary>
/// The portal being visited. Its name and logo are public (the sign-in screen shows them); the rest of its data is
/// edited by its Admin.
/// </summary>
[ApiController]
[Route("api/tenant")]
[AllowOnPlatform]
public sealed class TenantController(ITenantContext tenantContext, ITenantProfileService profileService, TenantCache cache) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var tenant = tenantContext.Tenant!;
        return Ok(new TenantResponse(tenant.Slug, tenant.Name, tenant.IsPlatform, tenant.LogoVersion));
    }

    /// <summary>Clients add ?v={logoVersion}: each version is cached for a day, a new logo gets a new URL.</summary>
    [HttpGet("logo")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLogo(CancellationToken cancellationToken)
    {
        var logo = await profileService.OpenLogoAsync(cancellationToken);
        if (logo is null)
            return NotFound();

        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        Response.Headers[HeaderNames.CacheControl] = "public, max-age=86400";
        return File(logo.Content, logo.ContentType);
    }

    [HttpGet("profile")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType<TenantProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken) =>
        Ok(await profileService.GetAsync(cancellationToken));

    [HttpPut("profile")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType<TenantProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile(UpdateTenantProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await profileService.UpdateAsync(request, cancellationToken);
        Refresh();
        return this.ToActionResult(result, Ok);
    }

    /// <summary>The size limit leaves room for the multipart envelope.</summary>
    [HttpPut("logo")]
    [Authorize(Policy = Policies.AdminOnly)]
    [RequestSizeLimit(ITenantProfileService.MaxLogoBytes + 64 * 1024)]
    [ProducesResponseType<TenantProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadLogo(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = ["Send the image in the \"file\" field."] }));

        await using var content = file.OpenReadStream();
        var result = await profileService.SetLogoAsync(content, cancellationToken);
        Refresh();
        return this.ToActionResult(result, Ok);
    }

    [HttpDelete("logo")]
    [Authorize(Policy = Policies.AdminOnly)]
    [ProducesResponseType<TenantProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveLogo(CancellationToken cancellationToken)
    {
        var profile = await profileService.RemoveLogoAsync(cancellationToken);
        Refresh();
        return Ok(profile);
    }

    /// <summary>
    /// This instance shows the change at once; the others within <see cref="TenancyOptions.CacheDuration"/> (minutes):
    /// name and logo are not urgent.
    /// </summary>
    private void Refresh() => cache.Forget(tenantContext.Tenant!.Slug);
}
