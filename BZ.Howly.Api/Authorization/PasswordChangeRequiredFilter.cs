using BZ.Howly.Api.Utils.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace BZ.Howly.Api.Authorization;

/// <summary>Marks endpoints a user with a temporary password can still use (e.g. to change it).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowPendingPasswordChangeAttribute : Attribute;

/// <summary>
/// Global filter: while the token says the password is temporary, every endpoint answers 403
/// except those marked with <see cref="AllowPendingPasswordChangeAttribute"/>.
/// </summary>
public sealed class PasswordChangeRequiredFilter(ProblemDetailsFactory problemDetailsFactory) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.User.HasClaim(JwtClaimTypes.PasswordChangeRequired, "true"))
            return;

        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowPendingPasswordChangeAttribute>().Any())
            return;

        var problem = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            title: "Password change required.",
            detail: "Change the temporary password at POST /api/auth/change-password before using the portal.");

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
    }
}
