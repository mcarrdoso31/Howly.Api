namespace BZ.Howly.Api.Core.DTO.Response;

public sealed record PreferencesResponse(string ThemeMode, string ThemePreset, string? PrimaryColor, string Language, DateTimeOffset UpdatedAt);
