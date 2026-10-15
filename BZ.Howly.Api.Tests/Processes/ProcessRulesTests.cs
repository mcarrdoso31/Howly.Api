using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

/// <summary>Rules decided on 2026-10-14: pause freezes the process, the customer only acts on their own step, and so on.</summary>
public class ProcessRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CustomerId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Actor Jane = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Jane Analyst");
    private static readonly Actor Caio = new(Guid.Parse("00000000-0000-0000-0000-0000000000a2"), "Caio Admin");
    private static readonly Actor Bia = new(Guid.Parse("00000000-0000-0000-0000-0000000000b1"), "Beagle Industrial Ltda");

    /// <summary>Customer step (5 days), then staff step (3 days), then an optional customer step.</summary>
    private static ProcessType Type() => ProcessType.Create("Emissão de MTR", null,
    [
        new("Coleta de documentos", null, StepOwner.Customer, null, 5, false, null, [], []),
        new("Protocolo", null, StepOwner.Staff, null, 3, false, null, [], []),
        new("Complemento", null, StepOwner.Customer, null, null, true, null, [], []),
    ], Now);

    private static Process Open(Process? parent = null, Guid? customerId = null) =>
        Process.Open(customerId ?? CustomerId, Type(), "MTR — Jundiaí", Jane.Id, parent, Jane, Now);

    private static StoredFile File() => new($"documents/{Guid.NewGuid():N}.bin", "licenca.pdf", 1024);

    private static ProcessDocument RequestedIn(Process process, int stepIndex) =>
        ProcessDocument.Requested(process.Id, process.Steps[stepIndex].Id, "Licença", true, 0);

    // ---- pause freezes everything, including the deadline ----

    [Fact]
    public void Pause_freezes_the_deadline_of_the_active_step()
    {
        var process = Open();

        process.Pause(Jane, "Cliente pediu para segurar", Now.AddDays(1));
        process.Resume(Jane, Now.AddDays(11));

        process.ActiveStepDueAt.Should().Be(Now.AddDays(15));
        process.ActiveStep!.DueAt.Should().Be(Now.AddDays(15));
        process.ActiveStep.StartedAt.Should().Be(Now, "the start date is kept for the record");
    }

    [Fact]
    public void Pauses_add_up_and_a_new_run_of_the_step_starts_clean()
    {
        var process = Open();
        process.Pause(Jane, "Pausa 1", Now.AddDays(1));
        process.Resume(Jane, Now.AddDays(3));
        process.Pause(Jane, "Pausa 2", Now.AddDays(4));
        process.Resume(Jane, Now.AddDays(5));

        process.ActiveStepDueAt.Should().Be(Now.AddDays(8));

        process.Advance(Jane, null, Now.AddDays(6));
        process.ReturnTo(process.Steps[0].Id, Jane, "Órgão pediu correção", Now.AddDays(7));

        process.ActiveStepDueAt.Should().Be(Now.AddDays(12));
    }

    [Fact]
    public void Paused_time_does_not_count_as_delay()
    {
        var process = Open();
        process.Pause(Jane, "Pausa", Now.AddDays(1));

        process.MarkOverdue(Now.AddDays(8)).Should().BeFalse("paused processes are never late");
        process.Resume(Jane, Now.AddDays(11));

        process.MarkOverdue(Now.AddDays(12)).Should().BeFalse("the deadline moved to day 15");
        process.MarkOverdue(Now.AddDays(16)).Should().BeTrue();
    }

    [Fact]
    public void Paused_and_finished_processes_take_no_document_changes()
    {
        var paused = Open();
        paused.Pause(Jane, "Pausa", Now);
        var cancelled = Open();
        cancelled.Cancel(Jane, "Desistiu", Now);

        Open().Invoking(p => p.RequireOpenForDocuments()).Should().NotThrow();
        paused.Invoking(p => p.RequireOpenForDocuments()).Should().Throw<ProcessRuleException>();
        cancelled.Invoking(p => p.RequireOpenForDocuments()).Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void Comments_are_allowed_while_paused_but_not_once_finished()
    {
        var paused = Open();
        paused.Pause(Jane, "Pausa", Now);
        var cancelled = Open();
        cancelled.Cancel(Jane, "Desistiu", Now);

        paused.Comment(Bia, "Quando retomamos?", Now);

        paused.Events.Last().Type.Should().Be(ProcessEventType.Commented);
        cancelled.Invoking(p => p.Comment(Jane, "Arquivado", Now)).Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void Internal_notes_stay_allowed_after_the_end()
    {
        var process = Open();
        process.Cancel(Jane, "Desistiu", Now);

        process.AddInternalNote(Jane, "Arquivado no drive", Now);

        process.Events.Last().Type.Should().Be(ProcessEventType.InternalNote);
    }

    // ---- the customer only sends files on their own step ----

    [Fact]
    public void The_customer_sends_documents_of_the_active_step_while_it_is_theirs()
    {
        var process = Open();

        process.Invoking(p => p.RequireCustomerCanSend(RequestedIn(process, 0))).Should().NotThrow();
        process.Invoking(p => p.RequireCustomerCanSend(null)).Should().NotThrow("extras are fine on the customer's turn");
    }

    [Fact]
    public void The_customer_cannot_send_when_the_step_is_not_theirs()
    {
        var process = Open();
        var document = RequestedIn(process, 0);
        process.Advance(Jane, null, Now);

        process.Invoking(p => p.RequireCustomerCanSend(document)).Should().Throw<ProcessRuleException>();
        process.Invoking(p => p.RequireCustomerCanSend(null)).Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void The_customer_cannot_send_documents_of_another_step()
    {
        var process = Open();

        process.Invoking(p => p.RequireCustomerCanSend(RequestedIn(process, 2))).Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void The_customer_cannot_replace_an_approved_document_nor_send_deliverables()
    {
        var process = Open();
        var approved = RequestedIn(process, 0);
        approved.Upload(File(), Bia, Now);
        approved.Approve(Jane, Now);
        var deliverable = ProcessDocument.Deliverable(process.Id, process.Steps[0].Id, "Protocolo", 0);

        process.Invoking(p => p.RequireCustomerCanSend(approved)).Should().Throw<ProcessRuleException>();
        process.Invoking(p => p.RequireCustomerCanSend(deliverable)).Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void The_customer_cannot_send_while_paused()
    {
        var process = Open();
        process.Pause(Jane, "Pausa", Now);

        process.Invoking(p => p.RequireCustomerCanSend(null)).Should().Throw<ProcessRuleException>();
    }

    // ---- assign ----

    [Fact]
    public void Assigning_the_current_assignee_records_nothing()
    {
        var process = Open();
        var events = process.Events.Count;

        process.Assign(Jane.Id, Jane.Name, Caio, Now);

        process.Events.Should().HaveCount(events);
    }

    [Fact]
    public void Paused_processes_can_be_reassigned_but_finished_ones_cannot()
    {
        var paused = Open();
        paused.Pause(Jane, "Pausa", Now);
        var cancelled = Open();
        cancelled.Cancel(Jane, "Desistiu", Now);

        paused.Assign(Caio.Id, Caio.Name, Jane, Now);

        paused.AssigneeId.Should().Be(Caio.Id);
        cancelled.Invoking(p => p.Assign(Caio.Id, Caio.Name, Jane, Now)).Should().Throw<ProcessRuleException>();
        Open().Invoking(p => p.Assign(Guid.Empty, "Ninguém", Jane, Now)).Should().Throw<ArgumentException>();
    }

    // ---- cancel ----

    [Fact]
    public void Cancel_closes_the_active_step()
    {
        var process = Open();

        process.Cancel(Jane, "Desistiu", Now.AddDays(1));

        process.Steps[0].Status.Should().Be(StepStatus.Cancelled);
        process.Steps[0].FinishedAt.Should().Be(Now.AddDays(1));
        process.ActiveStep.Should().BeNull();
    }

    // ---- subprocesses ----

    [Fact]
    public void Subprocesses_open_under_a_main_process_in_progress_of_the_same_customer()
    {
        var parent = Open();

        var child = Open(parent);

        child.ParentId.Should().Be(parent.Id);
    }

    [Fact]
    public void Subprocesses_cannot_open_under_a_paused_or_finished_parent()
    {
        var paused = Open();
        paused.Pause(Jane, "Pausa", Now);
        var cancelled = Open();
        cancelled.Cancel(Jane, "Desistiu", Now);

        FluentActions.Invoking(() => Open(paused)).Should().Throw<ProcessRuleException>();
        FluentActions.Invoking(() => Open(cancelled)).Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void Subprocesses_need_a_main_process_of_the_same_customer()
    {
        var parent = Open();
        var child = Open(parent);

        FluentActions.Invoking(() => Open(child)).Should().Throw<ProcessRuleException>();
        FluentActions.Invoking(() => Open(parent, Guid.NewGuid())).Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void A_parent_with_open_subprocesses_cannot_be_completed_nor_cancelled()
    {
        var process = Open();
        process.Advance(Jane, null, Now, openSubprocesses: 1);
        process.Advance(Jane, null, Now, openSubprocesses: 1);

        process.Invoking(p => p.Skip(Jane, null, Now, openSubprocesses: 1)).Should().Throw<ProcessRuleException>();
        process.Invoking(p => p.Advance(Jane, null, Now, openSubprocesses: 1)).Should().Throw<ProcessRuleException>();
        process.Invoking(p => p.Cancel(Jane, "Desistiu", Now, openSubprocesses: 1)).Should().Throw<ProcessRuleException>();

        process.Status.Should().Be(ProcessStatus.InProgress);
        process.ActiveStepName.Should().Be("Complemento");
        process.ActiveStep!.Status.Should().Be(StepStatus.Active);
    }

    // ---- skip and versions ----

    [Fact]
    public void Skipping_with_pending_documents_is_recorded_with_their_names()
    {
        var process = Open();
        process.Advance(Jane, null, Now);
        process.Advance(Jane, null, Now);

        process.Skip(Jane, null, Now, ["Licença"]);

        var skipped = process.Events.Single(e => e.Type == ProcessEventType.StepSkipped);
        skipped.Details.Should().Be("Licença");
    }

    [Fact]
    public void Every_change_gives_the_process_a_new_version()
    {
        var process = Open();
        var opened = process.Version;

        process.Comment(Jane, "Oi", Now);

        opened.Should().NotBe(Guid.Empty);
        process.Version.Should().NotBe(opened);
    }

    // ---- documents: waive, restore, reopen ----

    [Fact]
    public void Documents_never_sent_are_waived_when_their_step_ends_and_come_back_on_return()
    {
        var process = Open();
        var never = RequestedIn(process, 0);
        var rejected = RequestedIn(process, 0);
        rejected.Upload(File(), Bia, Now);
        rejected.Reject(Jane, "Vencido", Now);
        var approved = RequestedIn(process, 0);
        approved.Upload(File(), Bia, Now);
        approved.Approve(Jane, Now);

        foreach (var document in new[] { never, rejected, approved })
            document.Waive();

        never.Status.Should().Be(DocumentStatus.Waived);
        rejected.Status.Should().Be(DocumentStatus.Waived);
        approved.Status.Should().Be(DocumentStatus.Approved);
        never.IsPending.Should().BeFalse();

        never.Restore();
        rejected.Restore();

        never.Status.Should().Be(DocumentStatus.AwaitingUpload);
        rejected.Status.Should().Be(DocumentStatus.Rejected);
    }

    [Fact]
    public void The_staff_reopen_an_approved_document_for_review()
    {
        var process = Open();
        var document = RequestedIn(process, 0);
        document.Upload(File(), Bia, Now);
        document.Approve(Jane, Now);
        var version = document.Version;

        document.Reopen();

        document.Status.Should().Be(DocumentStatus.InReview);
        document.LatestVersion!.Status.Should().Be(DocumentStatus.InReview);
        document.LatestVersion.ReviewedAt.Should().BeNull();
        document.Version.Should().NotBe(version);
        RequestedIn(process, 0).Invoking(d => d.Reopen()).Should().Throw<ProcessRuleException>();

        process.RecordDocumentEvent(ProcessEventType.DocumentReopened, document.Name, Jane, Now);
        process.Events.Last().Type.Should().Be(ProcessEventType.DocumentReopened);
    }

    // ---- upload policy ----

    [Fact]
    public void A_huge_extension_does_not_break_the_file_name_cleaning()
    {
        var name = "a." + new string('x', 250);

        var clean = UploadPolicy.CleanFileName(name);

        clean.Length.Should().BeLessThanOrEqualTo(UploadPolicy.MaxFileNameLength);
        FluentActions.Invoking(() => UploadPolicy.Check(name, "%PDF"u8.ToArray())).Should().NotThrow();
    }
}
