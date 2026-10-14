namespace BZ.Howly.Api.Authorization;

public static class Policies
{
    /// <summary>Company staff: Admin or Analyst.</summary>
    public const string Internal = "Internal";

    public const string Customer = "Customer";

    public const string AdminOnly = "AdminOnly";
}
