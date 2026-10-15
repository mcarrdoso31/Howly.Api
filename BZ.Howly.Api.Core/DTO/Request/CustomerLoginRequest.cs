namespace BZ.Howly.Api.Core.DTO.Request;

/// <param name="Document">The customer's CPF or CNPJ, with or without punctuation.</param>
public sealed record CustomerLoginRequest(string Document, string Password);
