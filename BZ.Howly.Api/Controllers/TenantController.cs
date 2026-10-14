using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Tenancy;
using BZ.Howly.Api.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>The portal being visited (its name on the sign-in screen). Public: no data of the consultancy beyond its name.</summary>
[ApiController]
[Route("api/tenant")]
[AllowAnonymous]
[AllowOnPlatform]
public sealed class TenantController(ITenantContext tenantContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var tenant = tenantContext.Tenant!;
        return Ok(new TenantResponse(tenant.Slug, tenant.Name, tenant.IsPlatform));
    }
}
