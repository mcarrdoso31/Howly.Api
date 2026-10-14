namespace BZ.Howly.Api.Core.Customers;

/// <summary>Which customers a listing returns, by the active flag of their login.</summary>
public enum CustomerStatusFilter
{
    Active,
    Inactive,
    All,
}
