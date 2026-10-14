using BZ.Howly.Api.Core.Identity;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Identity;

public class UserPreferencesTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("light", "howly", null, "pt-BR")]
    [InlineData("dark", "forest", "#2F7D4A", "en")]
    [InlineData("system", "ocean", "#1f6fb2", "es")]
    [InlineData("dark", "graphite", null, "pt-BR")]
    public void Validate_accepts_supported_values(string mode, string preset, string? color, string language)
    {
        UserPreferences.Validate(mode, preset, color, language).Should().BeEmpty();
    }

    [Theory]
    [InlineData("neon", "howly", null, "pt-BR", "themeMode")]
    [InlineData("Dark", "howly", null, "pt-BR", "themeMode")]
    [InlineData("dark", "rainbow", null, "pt-BR", "themePreset")]
    [InlineData("dark", "howly", "red", "pt-BR", "primaryColor")]
    [InlineData("dark", "howly", "#12345", "pt-BR", "primaryColor")]
    [InlineData("dark", "howly", "#12345g", "pt-BR", "primaryColor")]
    [InlineData("dark", "howly", null, "fr", "language")]
    [InlineData("dark", "howly", null, "pt", "language")]
    [InlineData("dark", "howly", null, "", "language")]
    public void Validate_reports_the_invalid_field(string mode, string preset, string? color, string language, string field)
    {
        UserPreferences.Validate(mode, preset, color, language).Should().ContainKey(field).And.HaveCount(1);
    }

    [Fact]
    public void Create_normalizes_the_color_to_lowercase()
    {
        var preferences = UserPreferences.Create(UserId, "dark", "forest", "#2F7D4A", "en", Now);

        preferences.UserId.Should().Be(UserId);
        preferences.ThemeMode.Should().Be("dark");
        preferences.ThemePreset.Should().Be("forest");
        preferences.PrimaryColor.Should().Be("#2f7d4a");
        preferences.Language.Should().Be("en");
        preferences.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Update_replaces_every_value()
    {
        var preferences = UserPreferences.Create(UserId, "dark", "forest", "#2f7d4a", "en", Now);

        preferences.Update("light", "ocean", null, "es", Now.AddHours(1));

        preferences.Should().BeEquivalentTo(new
        {
            UserId,
            ThemeMode = "light",
            ThemePreset = "ocean",
            PrimaryColor = (string?)null,
            Language = "es",
            UpdatedAt = Now.AddHours(1),
        });
    }

    [Fact]
    public void Create_throws_for_invalid_values()
    {
        var act = () => UserPreferences.Create(UserId, "neon", "forest", null, "en", Now);

        act.Should().Throw<ArgumentException>();
    }
}
