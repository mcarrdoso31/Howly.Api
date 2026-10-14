using Microsoft.AspNetCore.Diagnostics;

namespace BZ.Howly.Api.ErrorHandling;

/// <summary>
/// The client gave up on the request (changed page, typed again in a search…) and the work was cancelled on purpose.
/// Nobody is waiting for an answer: end it quietly with 499 and log it at debug level only.
/// A cancellation the client did not ask for (an internal timeout) is a real failure and goes on to the next handler.
/// </summary>
public sealed class CanceledRequestHandler(ILogger<CanceledRequestHandler> logger) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not OperationCanceledException || !httpContext.RequestAborted.IsCancellationRequested)
            return ValueTask.FromResult(false);

        logger.LogDebug("Request {Method} {Path} cancelled by the client.", httpContext.Request.Method, httpContext.Request.Path);
        httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        return ValueTask.FromResult(true);
    }
}
