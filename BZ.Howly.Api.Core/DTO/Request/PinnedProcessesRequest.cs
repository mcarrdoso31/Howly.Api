namespace BZ.Howly.Api.Core.DTO.Request;

/// <summary>The whole list of pinned processes, in the order to show them (an empty list unpins everything).</summary>
public sealed record PinnedProcessesRequest(IReadOnlyList<Guid>? ProcessIds);
