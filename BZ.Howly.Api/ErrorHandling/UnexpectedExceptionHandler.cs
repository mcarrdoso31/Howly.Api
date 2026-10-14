using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.ErrorHandling;

/// <summary>
/// Last resort: anything not handled before is a bug or an outage. Answers 500 with the trace id (the same one in the
/// log, with the full stack trace) and keeps internal details out of the response outside development.
/// </summary>
public sealed class UnexpectedExceptionHandler(
    IProblemDetailsService problemDetails, ILogger<UnexpectedExceptionHandler> logger, IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unexpected error on {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            // The message may carry internals (SQL, paths, secrets): only while developing.
            Detail = environment.IsDevelopment() ? exception.Message : null,
        };
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }
}
