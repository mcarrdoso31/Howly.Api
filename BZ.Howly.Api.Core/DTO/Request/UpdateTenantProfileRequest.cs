namespace BZ.Howly.Api.Core.DTO.Request;

/// <summary>The consultancy's data, edited by its Admin. Only the name is required; the time zone defaults to Brasília.</summary>
public sealed record UpdateTenantProfileRequest(
    string? Name,
    string? LegalName,
    string? Cnpj,
    string? Email,
    string? Phone,
    string? PostalCode,
    string? Street,
    string? City,
    string? State,
    string? TimeZone);
