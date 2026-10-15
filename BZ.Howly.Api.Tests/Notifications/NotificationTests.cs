using BZ.Howly.Api.Core.Notifications;
using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Notifications;

public class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Actor Jane = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Jane Analyst");
    private static readonly Guid CustomerUser = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private static Process MtrProcess()
    {
        var type = ProcessType.Create("Emissão de MTR", null,
        [
            new("Coleta", null, StepOwner.Customer, null, 5, false, null, [], []),
            new("Análise", null, StepOwner.Agency, "IBAMA", 30, false, null, [], []),
        ], Now);
        return Process.Open(Guid.NewGuid(), type, "MTR — Jundiaí", Jane.Id, null, Jane, Now);
    }

    [Fact]
    public void Copies_what_the_message_needs_from_the_process_event()
    {
        var process = MtrProcess();
        process.Advance(Jane, "Documentos ok", Now.AddHours(1));
        var advanced = process.Events.Last();

        var notification = Notification.For(CustomerUser, process, advanced);

        notification.UserId.Should().Be(CustomerUser);
        notification.ProcessId.Should().Be(process.Id);
        notification.ProcessTitle.Should().Be("MTR — Jundiaí");
        notification.EventId.Should().Be(advanced.Id);
        notification.Type.Should().Be(ProcessEventType.StepAdvanced);
        notification.StepName.Should().Be("Coleta");
        notification.ActorName.Should().Be("Jane Analyst");
        notification.CreatedAt.Should().Be(Now.AddHours(1));
        notification.ReadAt.Should().BeNull();
    }

    [Fact]
    public void Reading_keeps_the_first_time_it_was_read()
    {
        var process = MtrProcess();
        var notification = Notification.For(CustomerUser, process, process.Events[0]);

        notification.MarkRead(Now.AddHours(2));
        notification.MarkRead(Now.AddHours(5));

        notification.ReadAt.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public void Recipients_are_the_customer_and_the_assignee_but_never_who_acted()
    {
        var assignee = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

        Notification.Recipients(CustomerUser, assignee, actorId: Jane.Id).Should().BeEquivalentTo([CustomerUser, assignee]);
        Notification.Recipients(CustomerUser, assignee, actorId: assignee).Should().Equal(CustomerUser);
        Notification.Recipients(CustomerUser, assignee, actorId: CustomerUser).Should().Equal(assignee);
        Notification.Recipients(null, assignee, actorId: Jane.Id).Should().Equal(assignee);
    }
}
