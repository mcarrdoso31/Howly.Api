namespace BZ.Howly.Api.Core.DTO.Response;

/// <summary>The signed-in customer's own registration data.</summary>
public sealed record CustomerProfileResponse(
    string Cnpj, string CompanyName, string? TradeName, IReadOnlyList<ContactResponse> Contacts, DateTimeOffset? ConfirmedAt);
