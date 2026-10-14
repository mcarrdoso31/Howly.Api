namespace BZ.Howly.Api.Utils.Security;

/// <summary>Short claim names used in Howly tokens. Inbound claim mapping is disabled, so these are read as-is.</summary>
public static class JwtClaimTypes
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";

    /// <summary>Present (value "true") while the user still has a temporary password.</summary>
    public const string PasswordChangeRequired = "pwd_change_required";

    /// <summary>Present (value "true") while a new customer has not confirmed their data.</summary>
    public const string ProfileConfirmationRequired = "profile_confirmation_required";

    /// <summary>The consultancy the token was issued in: it is only valid on that portal.</summary>
    public const string Tenant = "tid";
}
