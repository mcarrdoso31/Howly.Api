namespace BZ.Howly.Api.Core.DTO.Request;

public sealed record UpdatePreferencesRequest(string ThemeMode, string ThemePreset, string? PrimaryColor, string Language);
