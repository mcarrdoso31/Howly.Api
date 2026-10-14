namespace BZ.Howly.Api.Core.Shared.Exceptions;

/// <summary>
/// Exceptions with a meaning the client can act on; the API turns each one into its HTTP status (problem details).
/// Expected outcomes of use cases still travel as <see cref="OperationResult{T}"/>: these are for the cases that
/// cannot (a session that vanished mid-request, a rule broken deep in the domain…).
/// </summary>
/// <param name="code">Optional machine-readable reason for the client (e.g. "account_inactive").</param>
public abstract class HowlyException(string message, string? code = null) : Exception(message)
{
    public abstract int StatusCode { get; }
    public string? Code { get; } = code;
}

/// <summary>The session is not valid anymore (e.g. the signed-in user no longer exists). 401.</summary>
public class UnauthorizedException(string message, string? code = null) : HowlyException(message, code)
{
    public override int StatusCode => 401;
}

/// <summary>The signed-in user may not do this. 403.</summary>
public class ForbiddenException(string message, string? code = null) : HowlyException(message, code)
{
    public override int StatusCode => 403;
}

/// <summary>What was asked for does not exist. 404.</summary>
public class NotFoundException(string message, string? code = null) : HowlyException(message, code)
{
    public override int StatusCode => 404;
}

/// <summary>The current state does not allow this (e.g. advancing a cancelled process). 409.</summary>
public class ConflictException(string message, string? code = null) : HowlyException(message, code)
{
    public override int StatusCode => 409;
}

/// <summary>Invalid input, keyed by field like the request body. 400.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors, string message = "One or more fields are invalid.")
    : HowlyException(message)
{
    public override int StatusCode => 400;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
