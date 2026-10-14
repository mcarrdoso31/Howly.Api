using System.Security.Claims;
using BZ.Howly.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace BZ.Howly.Api.Tests.Identity;

public class ProfileConfirmationRequiredFilterTests
{
    private readonly ProfileConfirmationRequiredFilter _filter = new(new SimpleProblemDetailsFactory());

    private static AuthorizationFilterContext Context(string[] claims, params object[] endpointMetadata)
    {
        var identity = new ClaimsIdentity(claims.Select(type => new Claim(type, "true")), "test");
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var action = new ActionDescriptor { EndpointMetadata = endpointMetadata.ToList() };
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), action), []);
    }

    [Fact]
    public void Blocks_a_customer_that_has_not_confirmed_the_data()
    {
        var context = Context(["profile_confirmation_required"]);

        _filter.OnAuthorization(context);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        result.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be("Profile confirmation required.");
    }

    [Fact]
    public void Lets_through_endpoints_marked_as_allowed()
    {
        var context = Context(["profile_confirmation_required"], new AllowPendingProfileConfirmationAttribute());

        _filter.OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void Lets_through_users_without_the_claim()
    {
        var context = Context([]);

        _filter.OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public void Leaves_a_pending_password_change_to_the_password_filter()
    {
        // The password comes first: its own filter answers, with the message about the password.
        var context = Context(["profile_confirmation_required", "pwd_change_required"], new AllowPendingPasswordChangeAttribute());

        _filter.OnAuthorization(context);

        context.Result.Should().BeNull();
    }

    private sealed class SimpleProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext, int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Detail = detail };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext, ModelStateDictionary modelStateDictionary, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode, Title = title };
    }
}
