namespace BZ.Howly.Api.Core.Customers;

/// <summary>Read model for customer listings, joined with the login's status.</summary>
public sealed record CustomerListItem(
    Guid Id, string Cnpj, string CompanyName, bool IsActive, bool MustChangePassword, DateTimeOffset? ConfirmedAt);
