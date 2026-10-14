namespace BZ.Howly.Api.Core.Identity;

public enum PasswordChangeStatus
{
    Success,
    InvalidCurrentPassword,
    Rejected,
}

public sealed record PasswordChangeResult(PasswordChangeStatus Status, IReadOnlyList<string> Errors)
{
    public static readonly PasswordChangeResult Success = new(PasswordChangeStatus.Success, []);
    public static readonly PasswordChangeResult InvalidCurrentPassword = new(PasswordChangeStatus.InvalidCurrentPassword, []);
    public static PasswordChangeResult Rejected(params string[] errors) => new(PasswordChangeStatus.Rejected, errors);
}

/// <summary>Outcome of a password change. On success a fresh session replaces every previous one.</summary>
public sealed record ChangePasswordOutcome(PasswordChangeStatus Status, IReadOnlyList<string> Errors, AuthSession? Session);
