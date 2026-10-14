using BZ.Howly.Api.Core.Tenancy;

namespace BZ.Howly.Api.Core.DTO.Response;

/// <summary>The consultancy's data as stored (digits only for CNPJ, phone and postal code).</summary>
public sealed record TenantProfileResponse(
    string Slug,
    string Name,
    string? LegalName,
    string? Cnpj,
    string? Email,
    string? Phone,
    string? PostalCode,
    string? Street,
    string? City,
    string? State,
    string TimeZone,
    string? LogoVersion)
{
    public static TenantProfileResponse From(Tenant tenant) => new(
        tenant.Slug, tenant.Name, tenant.Profile.LegalName, tenant.Profile.Cnpj, tenant.Profile.Email, tenant.Profile.Phone,
        tenant.Profile.PostalCode, tenant.Profile.Street, tenant.Profile.City, tenant.Profile.State, tenant.Profile.TimeZone, tenant.LogoVersion);
}
