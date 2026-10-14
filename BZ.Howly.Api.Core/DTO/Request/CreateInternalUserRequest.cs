namespace BZ.Howly.Api.Core.DTO.Request;

/// <param name="Role">Analyst or Admin.</param>
public sealed record CreateInternalUserRequest(string Username, string DisplayName, string Role);

/// <summary>What the Admin can change on a staff account (contacts and photo are the person's own business).</summary>
public sealed record UpdateInternalUserRequest(string? DisplayName, string? Role);

/// <summary>Who inherits the open processes of the person leaving; required while they have any.</summary>
public sealed record DeactivateInternalUserRequest(Guid? ReassignTo);
