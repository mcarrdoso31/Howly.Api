namespace BZ.Howly.Api.Core.DTO.Request;

/// <summary>Editable customer data; the contact list replaces the current one. The CNPJ is not here on purpose: it is the customer's login.</summary>
public sealed record UpdateCustomerRequest(string CompanyName, IReadOnlyList<ContactRequest>? Contacts);
