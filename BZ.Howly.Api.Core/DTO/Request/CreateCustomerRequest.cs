namespace BZ.Howly.Api.Core.DTO.Request;

public sealed record CreateCustomerRequest(string Cnpj, string CompanyName, IReadOnlyList<ContactRequest>? Contacts);
