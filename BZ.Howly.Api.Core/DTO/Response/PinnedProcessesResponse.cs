namespace BZ.Howly.Api.Core.DTO.Response;

public sealed record PinnedProcessesResponse(IReadOnlyList<Guid> ProcessIds);
