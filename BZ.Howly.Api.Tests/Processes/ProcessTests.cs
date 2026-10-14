using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

public class ProcessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CustomerId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Actor Jane = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Jane Analyst");
    private static readonly Actor Caio = new(Guid.Parse("00000000-0000-0000-0000-0000000000a2"), "Caio Admin");

    private static ProcessType Mtr() => ProcessType.Create("Emissão de MTR", null,
    [
        new("Coleta de documentos", null, StepOwner.Customer, null, 5, false, "Procuração", null, [], []),
        new("Protocolo", null, StepOwner.Staff, null, 3, false, null, "Protocolo", [], []),
        new("Pagamento da guia", null, StepOwner.Payment, null, null, true, null, "Guia", [], []),
        new("Análise", null, StepOwner.Agency, "IBAMA", 30, false, null, "Licença", [], []),
    ], Now);

    private static Process Open(DateTimeOffset? at = null) => Process.Open(CustomerId, Mtr(), " MTR — Jundiaí ", Jane.Id, null, Jane, at ?? Now);

    private static string[] StatusOf(Process process) => process.Steps.Select(s => $"{s.Name}:{s.Status}").ToArray();

    // ---- opening ----

    [Fact]
    public void Open_copies_the_steps_and_activates_the_first()
    {
        var process = Open();

        process.Title.Should().Be("MTR — Jundiaí");
        process.ProcessTypeName.Should().Be("Emissão de MTR");
        process.Status.Should().Be(ProcessStatus.InProgress);
        process.AssigneeId.Should().Be(Jane.Id);
        StatusOf(process).Should().Equal("Coleta de documentos:Active", "Protocolo:Pending", "Pagamento da guia:Pending", "Análise:Pending");
        process.Steps[0].StartedAt.Should().Be(Now);
        process.Steps[3].AgencyName.Should().Be("IBAMA");
        process.Steps[0].CustomerChecklist.Should().Be("Procuração");
    }

    [Fact]
    public void Open_tells_who_the_process_is_waiting_on_and_until_when()
    {
        var process = Open();

        process.WaitingOn.Should().Be(StepOwner.Customer);
        process.ActiveStepName.Should().Be("Coleta de documentos");
        process.ActiveStepStartedAt.Should().Be(Now);
        process.ActiveStepDueAt.Should().Be(Now.AddDays(5));
    }

    [Fact]
    public void Open_records_the_first_event()
    {
        var process = Open();

        var opened = process.Events.Should().ContainSingle().Subject;
        opened.Type.Should().Be(ProcessEventType.Opened);
        opened.ActorName.Should().Be("Jane Analyst");
        opened.At.Should().Be(Now);
    }

    [Fact]
    public void Changing_the_type_later_does_not_touch_the_process()
    {
        var type = Mtr();
        var process = Process.Open(CustomerId, type, "MTR", Jane.Id, null, Jane, Now);

        type.Update("Outro", null, [new("Única", null, StepOwner.Staff, null, null, false, null, null, [], [])], Now);

        process.Steps.Should().HaveCount(4);
        process.ProcessTypeName.Should().Be("Emissão de MTR");
    }

    [Fact]
    public void Validate_requires_a_title()
    {
        Process.ValidateTitle(" ").Should().ContainKey("title");
        Process.ValidateTitle(new string('x', Process.MaxTitleLength + 1)).Should().ContainKey("title");
        Process.ValidateTitle("MTR").Should().BeEmpty();
    }

    // ---- advancing ----

    [Fact]
    public void Advance_finishes_the_active_step_and_starts_the_next()
    {
        var process = Open();

        process.Advance(Jane, "Documentos recebidos", Now.AddDays(2));

        StatusOf(process).Should().StartWith(["Coleta de documentos:Done", "Protocolo:Active"]);
        process.Steps[0].FinishedAt.Should().Be(Now.AddDays(2));
        process.Steps[1].StartedAt.Should().Be(Now.AddDays(2));
        process.WaitingOn.Should().Be(StepOwner.Staff);
        process.ActiveStepDueAt.Should().Be(Now.AddDays(5));
        var advanced = process.Events.Last();
        advanced.Type.Should().Be(ProcessEventType.StepAdvanced);
        advanced.StepName.Should().Be("Coleta de documentos");
        advanced.Comment.Should().Be("Documentos recebidos");
    }

    [Fact]
    public void A_step_without_deadline_has_no_due_date()
    {
        var process = Open();
        process.Advance(Jane, null, Now);
        process.Advance(Jane, null, Now);

        process.ActiveStepName.Should().Be("Pagamento da guia");
        process.ActiveStepDueAt.Should().BeNull();
    }

    [Fact]
    public void Advancing_past_the_last_step_completes_the_process()
    {
        var process = Open();

        for (var i = 0; i < 4; i++)
            process.Advance(Jane, null, Now.AddDays(i + 1));

        process.Status.Should().Be(ProcessStatus.Completed);
        process.CompletedAt.Should().Be(Now.AddDays(4));
        process.WaitingOn.Should().BeNull();
        process.ActiveStepName.Should().BeNull();
        process.Events.Last().Type.Should().Be(ProcessEventType.Completed);
    }

    // ---- skipping ----

    [Fact]
    public void Optional_steps_can_be_skipped()
    {
        var process = Open();
        process.Advance(Jane, null, Now);
        process.Advance(Jane, null, Now);

        process.Skip(Jane, "Isento de taxa", Now.AddDays(1));

        StatusOf(process)[2].Should().Be("Pagamento da guia:Skipped");
        process.ActiveStepName.Should().Be("Análise");
        process.Events.Last().Type.Should().Be(ProcessEventType.StepSkipped);
    }

    [Fact]
    public void Required_steps_cannot_be_skipped()
    {
        var act = () => Open().Skip(Jane, null, Now);

        act.Should().Throw<ProcessRuleException>();
    }

    // ---- returning ----

    [Fact]
    public void Return_reopens_an_earlier_step_and_resets_the_later_ones()
    {
        var process = Open();
        process.Advance(Jane, null, Now.AddDays(1));
        process.Advance(Jane, null, Now.AddDays(2));
        var first = process.Steps[0].Id;

        process.ReturnTo(first, Jane, "Procuração vencida", Now.AddDays(3));

        StatusOf(process).Should().Equal("Coleta de documentos:Active", "Protocolo:Pending", "Pagamento da guia:Pending", "Análise:Pending");
        process.Steps[0].StartedAt.Should().Be(Now.AddDays(3));
        process.Steps[0].FinishedAt.Should().BeNull();
        process.Steps[1].StartedAt.Should().BeNull();
        process.WaitingOn.Should().Be(StepOwner.Customer);
        var returned = process.Events.Last();
        returned.Type.Should().Be(ProcessEventType.StepReturned);
        returned.StepName.Should().Be("Coleta de documentos");
        returned.Comment.Should().Be("Procuração vencida");
    }

    [Fact]
    public void Return_needs_a_comment()
    {
        var process = Open();
        process.Advance(Jane, null, Now);

        var act = () => process.ReturnTo(process.Steps[0].Id, Jane, " ", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Return_only_goes_back()
    {
        var process = Open();
        process.Advance(Jane, null, Now);

        var toCurrent = () => process.ReturnTo(process.Steps[1].Id, Jane, "x", Now);
        var toFuture = () => process.ReturnTo(process.Steps[3].Id, Jane, "x", Now);
        var toUnknown = () => process.ReturnTo(Guid.NewGuid(), Jane, "x", Now);

        toCurrent.Should().Throw<ProcessRuleException>();
        toFuture.Should().Throw<ProcessRuleException>();
        toUnknown.Should().Throw<ProcessRuleException>();
    }

    // ---- pause, resume, cancel ----

    [Fact]
    public void Pause_stops_the_steps_until_resumed()
    {
        var process = Open();

        process.Pause(Jane, "Aguardando definição do cliente sobre a unidade", Now.AddDays(1));

        process.Status.Should().Be(ProcessStatus.Paused);
        process.StatusReason.Should().Be("Aguardando definição do cliente sobre a unidade");
        process.WaitingOn.Should().BeNull();
        var advance = () => process.Advance(Jane, null, Now);
        advance.Should().Throw<ProcessRuleException>();

        process.Resume(Jane, Now.AddDays(2));

        process.Status.Should().Be(ProcessStatus.InProgress);
        process.StatusReason.Should().BeNull();
        process.WaitingOn.Should().Be(StepOwner.Customer);
        process.Events.Select(e => e.Type).Should().EndWith([ProcessEventType.Paused, ProcessEventType.Resumed]);
    }

    [Fact]
    public void Cancel_is_final_and_keeps_the_reason()
    {
        var process = Open();

        process.Cancel(Jane, "Cliente desistiu", Now.AddDays(1));

        process.Status.Should().Be(ProcessStatus.Cancelled);
        process.StatusReason.Should().Be("Cliente desistiu");
        process.WaitingOn.Should().BeNull();
        var resume = () => process.Resume(Jane, Now);
        var advance = () => process.Advance(Jane, null, Now);
        resume.Should().Throw<ProcessRuleException>();
        advance.Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void Pause_and_cancel_need_a_reason()
    {
        var pause = () => Open().Pause(Jane, "", Now);
        var cancel = () => Open().Cancel(Jane, null, Now);

        pause.Should().Throw<ArgumentException>();
        cancel.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Paused_processes_can_be_cancelled()
    {
        var process = Open();
        process.Pause(Jane, "Pausa", Now);

        process.Cancel(Jane, "Encerrado", Now);

        process.Status.Should().Be(ProcessStatus.Cancelled);
    }

    // ---- assignee and comments ----

    [Fact]
    public void Assign_changes_the_responsible_person()
    {
        var process = Open();

        process.Assign(Caio.Id, Caio.Name, Jane, Now);

        process.AssigneeId.Should().Be(Caio.Id);
        var assigned = process.Events.Last();
        assigned.Type.Should().Be(ProcessEventType.Assigned);
        assigned.Comment.Should().Be("Caio Admin");
    }

    [Fact]
    public void Comments_go_to_the_timeline_until_the_process_is_finished()
    {
        var process = Open();

        process.Comment(Jane, "  Arquivado no drive  ", Now.AddDays(1));

        var comment = process.Events.Last();
        comment.Type.Should().Be(ProcessEventType.Commented);
        comment.Comment.Should().Be("Arquivado no drive");
        process.Cancel(Jane, "Encerrado", Now);
        process.Invoking(p => p.Comment(Jane, "Depois do fim", Now)).Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void Internal_notes_go_to_the_timeline_but_the_customer_never_sees_them()
    {
        var process = Open();

        process.AddInternalNote(Jane, "  Cliente costuma atrasar, cobrar por telefone  ", Now.AddHours(1));

        var note = process.Events.Last();
        note.Type.Should().Be(ProcessEventType.InternalNote);
        note.Comment.Should().Be("Cliente costuma atrasar, cobrar por telefone");
        note.IsVisibleToCustomer.Should().BeFalse();
        process.Events[0].IsVisibleToCustomer.Should().BeTrue();
    }

    [Fact]
    public void An_internal_note_needs_text()
    {
        var process = Open();

        process.Invoking(p => p.AddInternalNote(Jane, "   ", Now)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Renaming_keeps_the_old_title_on_the_timeline()
    {
        var process = Open();
        var oldTitle = process.Title;

        process.Rename(Jane, "  MTR — Jundiaí (filial)  ", Now.AddHours(1));

        process.Title.Should().Be("MTR — Jundiaí (filial)");
        var renamed = process.Events.Last();
        renamed.Type.Should().Be(ProcessEventType.Renamed);
        renamed.Comment.Should().Be("MTR — Jundiaí (filial)");
        renamed.Details.Should().Be(oldTitle);
    }

    [Fact]
    public void Renaming_to_the_same_title_records_nothing()
    {
        var process = Open();

        process.Rename(Jane, process.Title, Now.AddHours(1));

        process.Events.Should().ContainSingle();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_title_is_required(string? title)
    {
        var process = Open();

        process.Invoking(p => p.Rename(Jane, title, Now)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Events_come_in_order()
    {
        var process = Open();
        process.Advance(Jane, null, Now.AddMinutes(1));
        process.Comment(Jane, "ok", Now.AddMinutes(2));

        process.Events.Select(e => e.Type).Should().Equal(ProcessEventType.Opened, ProcessEventType.StepAdvanced, ProcessEventType.Commented);
    }

    // ---- documents ----

    [Fact]
    public void The_active_step_id_follows_the_steps()
    {
        var process = Open();
        process.ActiveStepId.Should().Be(process.Steps[0].Id);

        process.Advance(Jane, null, Now);
        process.ActiveStepId.Should().Be(process.Steps[1].Id);

        process.Pause(Jane, "Pausa", Now);
        process.ActiveStepId.Should().BeNull();
    }

    [Fact]
    public void Advancing_with_pending_documents_is_allowed_but_recorded()
    {
        var process = Open();

        process.Advance(Jane, "Cliente envia depois", Now, ["Procuração", "CTF"]);

        process.Steps[0].Status.Should().Be(StepStatus.Done);
        var advanced = process.Events.Last();
        advanced.Type.Should().Be(ProcessEventType.StepAdvancedWithPendingDocuments);
        advanced.StepName.Should().Be("Coleta de documentos");
        advanced.Comment.Should().Be("Cliente envia depois");
        advanced.Details.Should().Be("Procuração, CTF");
    }

    [Fact]
    public void Document_events_go_to_the_timeline()
    {
        var process = Open();

        process.RecordDocumentEvent(ProcessEventType.DocumentRejected, "Procuração", Jane, Now, "Sem assinatura");

        var rejected = process.Events.Last();
        rejected.Type.Should().Be(ProcessEventType.DocumentRejected);
        rejected.Details.Should().Be("Procuração");
        rejected.Comment.Should().Be("Sem assinatura");
    }

    [Fact]
    public void Only_document_events_can_be_recorded_from_outside()
    {
        var act = () => Open().RecordDocumentEvent(ProcessEventType.Completed, "x", Jane, Now);

        act.Should().Throw<ArgumentException>();
    }
}
