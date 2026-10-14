namespace BZ.Howly.Api.Core.DTO.Request;

/// <summary>Null or blank clears the trade name, so the company name is shown again.</summary>
public sealed record UpdateTradeNameRequest(string? TradeName);
