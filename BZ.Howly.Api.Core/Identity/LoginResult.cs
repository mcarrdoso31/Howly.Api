namespace BZ.Howly.Api.Core.Identity;

public enum LoginStatus
{
    Success,

    /// <summary>Unknown user, wrong password, wrong area or lockout: callers must not tell these apart.</summary>
    InvalidCredentials,

    /// <summary>The password was right but the account was deactivated by the staff.</summary>
    AccountInactive,
}

public sealed record LoginResult(LoginStatus Status, AuthSession? Session)
{
    public static readonly LoginResult InvalidCredentials = new(LoginStatus.InvalidCredentials, null);
    public static readonly LoginResult AccountInactive = new(LoginStatus.AccountInactive, null);
    public static LoginResult Success(AuthSession session) => new(LoginStatus.Success, session);
}
