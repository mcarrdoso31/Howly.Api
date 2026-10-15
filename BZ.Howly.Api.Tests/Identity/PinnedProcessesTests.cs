using BZ.Howly.Api.Core.Identity;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Identity;

/// <summary>The processes each staff member keeps on their home page: up to three, saved with their preferences.</summary>
public class PinnedProcessesTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-0000000000c4");
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    [Fact]
    public void Validate_accepts_none_to_three_distinct_processes()
    {
        UserPreferences.ValidatePins([]).Should().BeEmpty();
        UserPreferences.ValidatePins([Id(1), Id(2), Id(3)]).Should().BeEmpty();
    }

    [Fact]
    public void Validate_rejects_more_than_three()
    {
        UserPreferences.ValidatePins([Id(1), Id(2), Id(3), Id(4)]).Should().ContainKey("processIds");
    }

    [Fact]
    public void Validate_rejects_a_process_pinned_twice()
    {
        UserPreferences.ValidatePins([Id(1), Id(1)]).Should().ContainKey("processIds");
    }

    [Fact]
    public void Validate_requires_the_list()
    {
        UserPreferences.ValidatePins(null).Should().ContainKey("processIds");
    }

    [Fact]
    public void Pinning_before_choosing_a_look_keeps_the_look_unsaved()
    {
        var preferences = UserPreferences.CreateWithPins(UserId, [Id(2), Id(1)], Now);

        preferences.PinnedProcessIds.Should().Equal(Id(2), Id(1));
        preferences.HasAppearance.Should().BeFalse();
    }

    [Fact]
    public void Saving_the_look_keeps_the_pins()
    {
        var preferences = UserPreferences.CreateWithPins(UserId, [Id(1)], Now);

        preferences.Update("dark", "forest", null, "en", Now);

        preferences.HasAppearance.Should().BeTrue();
        preferences.PinnedProcessIds.Should().Equal(Id(1));
    }

    [Fact]
    public void Pinning_replaces_the_whole_list_and_keeps_the_look()
    {
        var preferences = UserPreferences.Create(UserId, "dark", "forest", null, "en", Now);

        preferences.Pin([Id(3), Id(1)]);
        preferences.Pin([Id(1)]);

        preferences.PinnedProcessIds.Should().Equal(Id(1));
        preferences.ThemeMode.Should().Be("dark");
    }

    [Fact]
    public void Pinning_an_invalid_list_throws()
    {
        var preferences = UserPreferences.Create(UserId, "dark", "forest", null, "en", Now);

        var act = () => preferences.Pin([Id(1), Id(2), Id(3), Id(4)]);

        act.Should().Throw<ArgumentException>();
    }
}
