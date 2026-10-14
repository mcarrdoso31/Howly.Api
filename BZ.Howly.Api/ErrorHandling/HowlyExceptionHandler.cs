using BZ.Howly.Api.Core.Shared.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BZ.Howly.Api.ErrorHandling;

/// <summary>
/// Exceptions with a known meaning become their status, as problem details: ours (<see cref="HowlyException"/>) and
/// the requests the server itself refused (too large, malformed). Expected, so logged as warnings.
/// </summary>
public sealed class HowlyExceptionHandler(IProblemDetailsService problemDetails, ILogger<HowlyExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException validation => new HttpValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            {
                Status = validation.StatusCode,
                Title = validation.Message,
            },
            HowlyException howly => new ProblemDetails { Status = howly.StatusCode, Title = howly.Message },
            BadHttpRequestException badRequest => new ProblemDetails { Status = badRequest.StatusCode, Title = badRequest.Message },
            _ => null,
        };
        if (problem is null)
            return false;

        if (exception is HowlyException { Code: { } code })
            problem.Extensions["code"] = code;

        logger.LogWarning("{Method} {Path} answered {Status}: {Message}", httpContext.Request.Method, httpContext.Request.Path, problem.Status, exception.Message);
        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }
}
