namespace BZ.Howly.Api.Core.DTO.Response;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <param name="TemporaryPassword">Shown only in this response. It is never stored or returned again.</param>
public sealed record TemporaryPasswordResponse(string TemporaryPassword);
