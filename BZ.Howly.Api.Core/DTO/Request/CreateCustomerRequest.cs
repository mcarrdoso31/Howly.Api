namespace BZ.Howly.Api.Core.DTO.Request;

/// <param name="Document">The CPF of a person or the CNPJ of a company, with or without punctuation.</param>
/// <param name="CompanyName">The company name, or the person's full name.</param>
public sealed record CreateCustomerRequest(string Document, string CompanyName, IReadOnlyList<ContactRequest>? Contacts);
