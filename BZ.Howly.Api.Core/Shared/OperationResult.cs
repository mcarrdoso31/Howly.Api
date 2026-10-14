namespace BZ.Howly.Api.Core.Shared;

public enum OperationStatus
{
    Success,
    NotFound,
    Conflict,
    ValidationFailed,
}

/// <summary>Outcome of an application use case, translated to HTTP by the API layer.</summary>
public sealed record OperationResult<T>(
    OperationStatus Status, T? Value, string? Message, IReadOnlyDictionary<string, string[]> Errors)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    public static OperationResult<T> Success(T value) => new(OperationStatus.Success, value, null, NoErrors);
    public static OperationResult<T> NotFound() => new(OperationStatus.NotFound, default, null, NoErrors);
    public static OperationResult<T> Conflict(string message) => new(OperationStatus.Conflict, default, message, NoErrors);

    public static OperationResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(OperationStatus.ValidationFailed, default, null, errors);

    public static OperationResult<T> Invalid(string field, params string[] messages) =>
        Invalid(new Dictionary<string, string[]> { [field] = messages });
}
