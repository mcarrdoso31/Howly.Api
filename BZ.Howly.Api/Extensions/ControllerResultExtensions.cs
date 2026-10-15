using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Utils.Security;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.Extensions;

public static class ControllerResultExtensions
{
    /// <summary>Maps a use-case outcome to the matching HTTP response (problem details for every failure).</summary>
    public static IActionResult ToActionResult<T>(this ControllerBase controller, OperationResult<T> result, Func<T, IActionResult> onSuccess) =>
        result.Status switch
        {
            OperationStatus.Success => onSuccess(result.Value!),
            OperationStatus.NotFound => controller.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found."),
            OperationStatus.Conflict => controller.Problem(statusCode: StatusCodes.Status409Conflict, title: result.Message),
            OperationStatus.ValidationFailed => controller.ValidationProblem(new ValidationProblemDetails(
                result.Errors.ToDictionary(e => e.Key, e => e.Value))),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null),
        };

    /// <summary>The process version the client acted on (If-Match), as sent; null when it did not say (nothing is checked then).</summary>
    public static string? IfMatch(this ControllerBase controller) =>
        controller.Request.Headers.IfMatch is { Count: > 0 } ifMatch ? ifMatch.ToString() : null;

    /// <summary>The process as JSON with its version as ETag, so a later If-Match can say which one the screen showed.</summary>
    public static IActionResult OkWithETag(this ControllerBase controller, ProcessResponse process)
    {
        if (process.Version is { } version)
            controller.Response.Headers.ETag = $"\"{version}\"";
        return controller.Ok(process);
    }

    public static Guid CurrentUserId(this ControllerBase controller) =>
        Guid.Parse(controller.User.FindFirst(JwtClaimTypes.Subject)!.Value);
}
