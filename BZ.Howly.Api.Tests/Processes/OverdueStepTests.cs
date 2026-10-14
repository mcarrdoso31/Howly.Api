using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

public class OverdueStepTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Actor Jane = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Jane Analyst");

    /// <summary>Customer step of 5 days, then a staff step of 3 days, then a step without deadline.</summary>
    private static Process Open() => Process.Open(Guid.NewGuid(), ProcessType.Create("Emissão de MTR", null,
    [
        new("Coleta de documentos", null, StepOwner.Customer, null, 5, false, null, null, [], []),
        new("Protocolo", null, StepOwner.Staff, null, 3, false, null, null, [], []),
        new("Análise", null, StepOwner.Agency, "IBAMA", null, false, null, null, [], []),
    ], Now), "MTR — Jundiaí", Jane.Id, null, Jane, Now);

    [Fact]
    public void A_step_past_its_deadline_is_recorded_once_by_the_system()
    {
        var process = Open();

        process.MarkOverdue(Now.AddDays(6)).Should().BeTrue();
        process.MarkOverdue(Now.AddDays(7)).Should().BeFalse();

        var overdue = process.Events.Last();
        overdue.Type.Should().Be(ProcessEventType.StepOverdue);
        overdue.StepName.Should().Be("Coleta de documentos");
        overdue.ActorId.Should().Be(Actor.System.Id);
        overdue.ActorName.Should().Be("Howly");
        process.Events.Count(e => e.Type == ProcessEventType.StepOverdue).Should().Be(1);
    }

    [Fact]
    public void Nothing_happens_before_the_deadline_nor_without_one_nor_while_paused()
    {
        var early = Open();
        early.MarkOverdue(Now.AddDays(4)).Should().BeFalse();

        var paused = Open();
        paused.Pause(Jane, "Aguardando cliente", Now.AddDays(1));
        paused.MarkOverdue(Now.AddDays(6)).Should().BeFalse();

        var noDeadline = Open();
        noDeadline.Advance(Jane, null, Now.AddDays(1));
        noDeadline.Advance(Jane, null, Now.AddDays(2));
        noDeadline.MarkOverdue(Now.AddDays(90)).Should().BeFalse();
    }

    [Fact]
    public void Going_back_to_a_step_restarts_its_clock_and_its_warning()
    {
        var process = Open();
        process.MarkOverdue(Now.AddDays(6));
        process.Advance(Jane, null, Now.AddDays(7));
        process.ReturnTo(process.Steps[0].Id, Jane, "Procuração vencida", Now.AddDays(8));

        process.MarkOverdue(Now.AddDays(10)).Should().BeFalse("the step started again on day 8");
        process.MarkOverdue(Now.AddDays(14)).Should().BeTrue();
    }

    [Fact]
    public void Only_the_customer_s_own_delays_are_shown_to_the_customer()
    {
        var customerStep = Open();
        customerStep.MarkOverdue(Now.AddDays(6));
        customerStep.Events.Last().IsVisibleToCustomer.Should().BeTrue();

        var staffStep = Open();
        staffStep.Advance(Jane, null, Now.AddDays(1));
        staffStep.MarkOverdue(Now.AddDays(5));
        staffStep.Events.Last().Type.Should().Be(ProcessEventType.StepOverdue);
        staffStep.Events.Last().IsVisibleToCustomer.Should().BeFalse();
    }
}
