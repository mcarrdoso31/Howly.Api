namespace BZ.Howly.Api.Core.Identity;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Analyst = "Analyst";
    public const string Customer = "Customer";

    /// <summary>The platform owners; they exist only in the platform tenant (the future super admin panel).</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    public static readonly IReadOnlyList<string> All = [Admin, Analyst, Customer, PlatformAdmin];
    public static readonly IReadOnlyList<string> Internal = [Admin, Analyst];
}
