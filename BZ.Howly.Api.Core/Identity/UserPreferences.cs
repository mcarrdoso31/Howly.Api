using System.Text.RegularExpressions;

namespace BZ.Howly.Api.Core.Identity;

/// <summary>Per-user look and language of the web app. Stored only once the user saves something.</summary>
public sealed partial class UserPreferences
{
    public static readonly IReadOnlyList<string> ThemeModes = ["light", "dark", "system"];
    public static readonly IReadOnlyList<string> ThemePresets = ["howly", "forest", "ocean", "graphite"];
    public static readonly IReadOnlyList<string> Languages = ["pt-BR", "en", "es"];

    public Guid UserId { get; private set; }
    public string ThemeMode { get; private set; } = string.Empty;
    public string ThemePreset { get; private set; } = string.Empty;

    /// <summary>Custom brand color as #rrggbb (lowercase). Null means the preset color is used.</summary>
    public string? PrimaryColor { get; private set; }

    public string Language { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    private UserPreferences() { }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    public static IReadOnlyDictionary<string, string[]> Validate(string? themeMode, string? themePreset, string? primaryColor, string? language)
    {
        var errors = new Dictionary<string, string[]>();

        if (themeMode is null || !ThemeModes.Contains(themeMode))
            errors["themeMode"] = [$"Theme mode must be one of: {string.Join(", ", ThemeModes)}."];
        if (themePreset is null || !ThemePresets.Contains(themePreset))
            errors["themePreset"] = [$"Theme preset must be one of: {string.Join(", ", ThemePresets)}."];
        if (primaryColor is not null && !HexColor().IsMatch(primaryColor))
            errors["primaryColor"] = ["Primary color must be a hex color like #2f7d4a."];
        if (language is null || !Languages.Contains(language))
            errors["language"] = [$"Language must be one of: {string.Join(", ", Languages)}."];

        return errors;
    }

    public static UserPreferences Create(
        Guid userId, string themeMode, string themePreset, string? primaryColor, string language, DateTimeOffset now)
    {
        var preferences = new UserPreferences { UserId = userId };
        preferences.Update(themeMode, themePreset, primaryColor, language, now);
        return preferences;
    }

    public void Update(string themeMode, string themePreset, string? primaryColor, string language, DateTimeOffset now)
    {
        var errors = Validate(themeMode, themePreset, primaryColor, language);
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid preferences: {string.Join(", ", errors.Keys)}.");

        ThemeMode = themeMode;
        ThemePreset = themePreset;
        PrimaryColor = primaryColor?.ToLowerInvariant();
        Language = language;
        UpdatedAt = now;
    }
}
