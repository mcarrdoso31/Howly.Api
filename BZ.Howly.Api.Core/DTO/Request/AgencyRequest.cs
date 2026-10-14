using BZ.Howly.Api.Core.Agencies;

namespace BZ.Howly.Api.Core.DTO.Request;

/// <summary>Create or replace an agency of the catalog.</summary>
public sealed record AgencyRequest(
    string? Acronym, string? Name, AgencySphere? Sphere, string? State, string? City, string? Website, string? ProtocolSystem)
{
    public AgencyDetails ToDetails() => new(Acronym, Name, Sphere, State, City, Website, ProtocolSystem);
}

/// <summary>The agency of an agency step of a process.</summary>
public sealed record SetStepAgencyRequest(Guid AgencyId);
