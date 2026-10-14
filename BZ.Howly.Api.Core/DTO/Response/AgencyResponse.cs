using BZ.Howly.Api.Core.Agencies;

namespace BZ.Howly.Api.Core.DTO.Response;

public sealed record AgencyResponse(
    Guid Id, string Acronym, string Name, AgencySphere Sphere, string? State, string? City, string? Website, string? ProtocolSystem, bool IsActive)
{
    public static AgencyResponse From(Agency a) => new(a.Id, a.Acronym, a.Name, a.Sphere, a.State, a.City, a.Website, a.ProtocolSystem, a.IsActive);
}
