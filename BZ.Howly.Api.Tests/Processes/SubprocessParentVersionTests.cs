using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

/// <summary>
/// Opening a subprocess changes the parent's version: completing or cancelling the parent at the same moment then
/// conflicts, instead of leaving a finished parent with an open child.
/// </summary>
public class SubprocessParentVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Actor Jane = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Jane Analyst");

    private static ProcessType Type() => ProcessType.Create("LO", null,
    [
        new("Análise", null, StepOwner.Staff, null, null, false, null, [], []),
    ], Now);

    [Fact]
    public void Opening_a_subprocess_changes_the_version_of_the_parent()
    {
        var customerId = Guid.NewGuid();
        var parent = Process.Open(customerId, Type(), "LO — Jundiaí", Jane.Id, null, Jane, Now);
        var versionBefore = parent.Version;

        Process.Open(customerId, Type(), "Outorga", Jane.Id, parent, Jane, Now.AddHours(1));

        parent.Version.Should().NotBe(versionBefore);
    }

    [Fact]
    public void The_parent_timeline_and_last_change_stay_as_they_were()
    {
        var customerId = Guid.NewGuid();
        var parent = Process.Open(customerId, Type(), "LO — Jundiaí", Jane.Id, null, Jane, Now);
        var events = parent.Events.Count;
        var updatedAt = parent.UpdatedAt;

        Process.Open(customerId, Type(), "Outorga", Jane.Id, parent, Jane, Now.AddHours(1));

        parent.Events.Should().HaveCount(events);
        parent.UpdatedAt.Should().Be(updatedAt);
    }
}
