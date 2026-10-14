namespace BZ.Howly.Api.Core.Identity;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Analyst = "Analyst";
    public const string Customer = "Customer";

    public static readonly IReadOnlyList<string> All = [Admin, Analyst, Customer];
    public static readonly IReadOnlyList<string> Internal = [Admin, Analyst];
}
