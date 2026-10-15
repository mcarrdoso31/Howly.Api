using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

/// <summary>A pause says why, who the process waits on and when it is expected back; on that day the staff are told.</summary>
public class PauseTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);
    private static readonly Actor Jane = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Jane Analyst");

    private static Process Open() => Process.Open(Guid.NewGuid(), ProcessType.Create("LO", null,
    [
        new("Análise", null, StepOwner.Agency, "IAT", 90, false, null, null, [], []),
    ], Now), "LO — Curitiba", Jane.Id, null, Jane, Now);

    private static PauseDetails WaitingOnIat(DateOnly? expectedReturn = null) =>
        new(PauseWaitingOn.Agency, Guid.NewGuid(), "IAT", expectedReturn ?? new DateOnly(2027, 1, 10));

    [Fact]
    public void A_pause_keeps_who_it_waits_on_and_the_expected_return()
    {
        var process = Open();

        process.Pause(Jane, "Recesso do IAT", Now, WaitingOnIat(), Today);

        process.StatusReason.Should().Be("Recesso do IAT");
        process.PauseWaitingOn.Should().Be(PauseWaitingOn.Agency);
        process.PauseWaitingOnName.Should().Be("IAT");
        process.PauseAgencyId.Should().NotBeNull();
        process.ExpectedReturnOn.Should().Be(new DateOnly(2027, 1, 10));
        var paused = process.Events.Last();
        paused.Comment.Should().Be("Recesso do IAT");
        paused.Details.Should().Be("IAT");
    }

    [Fact]
    public void Who_and_when_are_optional()
    {
        var process = Open();

        process.Pause(Jane, "Cliente pediu para segurar", Now);

        process.PauseWaitingOn.Should().BeNull();
        process.ExpectedReturnOn.Should().BeNull();
    }

    [Fact]
    public void Waiting_on_the_customer_needs_no_name_and_others_need_one()
    {
        var customer = Open();
        customer.Pause(Jane, "Aguardando assinatura", Now, new PauseDetails(PauseWaitingOn.Customer, null, "ignorado", null), Today);
        customer.PauseWaitingOnName.Should().BeNull();

        Open().Invoking(p => p.Pause(Jane, "Laboratório", Now, new PauseDetails(PauseWaitingOn.Other, null, " ", null), Today))
            .Should().Throw<ArgumentException>();
        Open().Invoking(p => p.Pause(Jane, "Órgão", Now, new PauseDetails(PauseWaitingOn.Agency, null, null, null), Today))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_expected_return_cannot_be_in_the_past()
    {
        var pause = () => Open().Pause(Jane, "Recesso", Now, WaitingOnIat(Today.AddDays(-1)), Today);

        pause.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resuming_or_cancelling_clears_the_pause()
    {
        var resumed = Open();
        resumed.Pause(Jane, "Recesso", Now, WaitingOnIat(), Today);
        resumed.Resume(Jane, Now.AddDays(1));
        var cancelled = Open();
        cancelled.Pause(Jane, "Recesso", Now, WaitingOnIat(), Today);
        cancelled.Cancel(Jane, "Desistiu", Now.AddDays(1));

        foreach (var process in new[] { resumed, cancelled })
        {
            process.PauseWaitingOn.Should().BeNull();
            process.PauseWaitingOnName.Should().BeNull();
            process.PauseAgencyId.Should().BeNull();
            process.ExpectedReturnOn.Should().BeNull();
        }
    }

    [Fact]
    public void On_the_expected_day_the_staff_are_told_once()
    {
        var process = Open();
        process.Pause(Jane, "Recesso do IAT", Now, WaitingOnIat(new DateOnly(2027, 1, 10)), Today);

        process.MarkPauseReturnDue(new DateOnly(2027, 1, 9), Now).Should().BeFalse("not yet");
        process.MarkPauseReturnDue(new DateOnly(2027, 1, 10), Now).Should().BeTrue();
        process.MarkPauseReturnDue(new DateOnly(2027, 1, 11), Now).Should().BeFalse("only once");

        var due = process.Events.Last();
        due.Type.Should().Be(ProcessEventType.PauseReturnDue);
        due.ActorName.Should().Be("Howly");
        due.IsVisibleToCustomer.Should().BeFalse("a reminder for the staff");
        process.Status.Should().Be(ProcessStatus.Paused, "the staff decide when to resume");
    }

    [Fact]
    public void Pauses_without_an_expected_return_are_never_due()
    {
        var process = Open();
        process.Pause(Jane, "Recesso", Now);

        process.MarkPauseReturnDue(Today.AddYears(1), Now).Should().BeFalse();
    }
}
