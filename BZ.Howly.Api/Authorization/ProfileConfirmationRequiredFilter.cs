using BZ.Howly.Api.Utils.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace BZ.Howly.Api.Authorization;

/// <summary>Marks endpoints a customer can use before confirming their data (e.g. to read and confirm it).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowPendingProfileConfirmationAttribute : Attribute;

/// <summary>
/// Global filter: while the token says a new customer has not confirmed their data, every endpoint answers 403
/// except those marked with <see cref="AllowPendingProfileConfirmationAttribute"/>.
/// A pending password change comes first and is left to <see cref="PasswordChangeRequiredFilter"/>.
/// </summary>
public sealed class ProfileConfirmationRequiredFilter(ProblemDetailsFactory problemDetailsFactory) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (!user.HasClaim(JwtClaimTypes.ProfileConfirmationRequired, "true") || user.HasClaim(JwtClaimTypes.PasswordChangeRequired, "true"))
            return;

        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowPendingProfileConfirmationAttribute>().Any())
            return;

        var problem = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            title: "Profile confirmation required.",
            detail: "Confirm your data at POST /api/me/company/confirm before using the portal.");

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
    }
}
