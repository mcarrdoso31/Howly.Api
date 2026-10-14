using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Controllers;

/// <summary>
/// The active staff members as options (e.g. to pick a process assignee). Any staff member can read it; managing the
/// team stays at api/internal-users, for Admins only.
/// </summary>
[ApiController]
[Route("api/staff")]
[Authorize(Policy = Policies.Internal)]
public sealed class StaffController(IInternalUserService internalUserService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PersonResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await internalUserService.ListActiveStaffAsync(cancellationToken));
}
