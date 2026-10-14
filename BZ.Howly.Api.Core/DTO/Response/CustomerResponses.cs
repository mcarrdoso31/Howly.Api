namespace BZ.Howly.Api.Core.DTO.Response;

/// <param name="TemporaryPassword">Shown only in this response. It is never stored or returned again.</param>
public sealed record CustomerCreatedResponse(
    Guid Id, string Cnpj, string CompanyName, IReadOnlyList<ContactResponse> Contacts, string TemporaryPassword);

public sealed record CustomerResponse(
    Guid Id, string Cnpj, string CompanyName, string? TradeName, IReadOnlyList<ContactResponse> Contacts,
    bool IsActive, bool MustChangePassword, DateTimeOffset? ConfirmedAt, DateTimeOffset CreatedAt);

public sealed record CustomerListItemResponse(
    Guid Id, string Cnpj, string CompanyName, bool IsActive, bool MustChangePassword, DateTimeOffset? ConfirmedAt);
