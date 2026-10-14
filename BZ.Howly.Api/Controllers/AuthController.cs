using BZ.Howly.Api.Authorization;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Extensions;
using BZ.Howly.Api.Utils.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BZ.Howly.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowPendingPasswordChange]
[AllowPendingProfileConfirmation]
public sealed class AuthController(IAuthService authService, IOptions<RefreshCookieOptions> cookieOptions) : ControllerBase
{
    private RefreshCookieOptions Cookie => cookieOptions.Value;

    [HttpPost("customer/login")]
    [AllowAnonymous]
    [EnableRateLimiting(ServiceCollectionExtensions.LoginRateLimitPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LoginCustomer(CustomerLoginRequest request, CancellationToken cancellationToken) =>
        ToLoginResponse(await authService.LoginCustomerAsync(request, cancellationToken));

    [HttpPost("internal/login")]
    [AllowAnonymous]
    [EnableRateLimiting(ServiceCollectionExtensions.LoginRateLimitPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LoginInternal(InternalLoginRequest request, CancellationToken cancellationToken) =>
        ToLoginResponse(await authService.LoginInternalAsync(request, cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(Cookie.Name, out var refreshToken) || string.IsNullOrEmpty(refreshToken))
            return InvalidSession();

        var session = await authService.RefreshAsync(refreshToken, cancellationToken);
        if (session is null)
        {
            DeleteRefreshCookie();
            return InvalidSession();
        }

        return StartSession(session);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(Cookie.Name, out var refreshToken) && !string.IsNullOrEmpty(refreshToken))
            await authService.LogoutAsync(refreshToken, cancellationToken);

        DeleteRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(JwtClaimTypes.Subject)?.Value, out var userId))
            return Unauthorized();

        var user = await authService.GetUserAsync(userId, cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(ServiceCollectionExtensions.LoginRateLimitPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var outcome = await authService.ChangePasswordAsync(this.CurrentUserId(), request, cancellationToken);

        return outcome.Status switch
        {
            PasswordChangeStatus.Success => StartSession(outcome.Session),
            PasswordChangeStatus.InvalidCurrentPassword => ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["currentPassword"] = ["Current password is incorrect."] })),
            _ => ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["newPassword"] = outcome.Errors.ToArray() })),
        };
    }

    /// <summary>Error code the web client uses to explain an inactive account (the title is not meant for users).</summary>
    public const string AccountInactiveCode = "account_inactive";

    private IActionResult ToLoginResponse(LoginResult result)
    {
        if (result.Status != LoginStatus.AccountInactive)
            return StartSession(result.Session);

        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status403Forbidden, "Account inactive.");
        problem.Extensions["code"] = AccountInactiveCode;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
    }

    private IActionResult StartSession(AuthSession? session)
    {
        if (session is null)
            return InvalidCredentials();

        Response.Cookies.Append(Cookie.Name, session.RefreshToken, BuildCookieOptions(session.RefreshTokenExpiresAt));
        return Ok(session.Response);
    }

    private void DeleteRefreshCookie() =>
        Response.Cookies.Delete(Cookie.Name, BuildCookieOptions(expiresAt: null));

    private CookieOptions BuildCookieOptions(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = Cookie.SameSite,
        Path = RefreshCookieOptions.Path,
        Expires = expiresAt,
    };

    private ObjectResult InvalidCredentials() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials.");

    private ObjectResult InvalidSession() =>
        Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Session expired. Please sign in again.");
}
