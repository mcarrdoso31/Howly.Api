using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

public class ProcessDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ProcessId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    private static readonly Guid StepId = Guid.Parse("00000000-0000-0000-0000-0000000000f2");
    private static readonly Actor Bia = new(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), "Beagle Industrial Ltda");
    private static readonly Actor Jane = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Jane Analyst");

    private static StoredFile File(string name = "licenca.pdf") => new($"documents/{Guid.NewGuid():N}.bin", name, 1024);

    private static ProcessDocument Requested(bool required = true) =>
        ProcessDocument.Requested(ProcessId, StepId, "Licença de operação de máquinas agrícolas", required, 0);

    [Fact]
    public void A_requested_document_waits_for_the_upload()
    {
        var document = Requested();

        document.Kind.Should().Be(DocumentKind.Requested);
        document.Status.Should().Be(DocumentStatus.AwaitingUpload);
        document.IsRequired.Should().BeTrue();
        document.Versions.Should().BeEmpty();
    }

    [Fact]
    public void Uploading_puts_it_in_review_as_version_1()
    {
        var document = Requested();

        document.Upload(File(), Bia, Now);

        document.Status.Should().Be(DocumentStatus.InReview);
        var version = document.Versions.Should().ContainSingle().Subject;
        version.Number.Should().Be(1);
        version.FileName.Should().Be("licenca.pdf");
        version.UploadedByName.Should().Be("Beagle Industrial Ltda");
        version.Status.Should().Be(DocumentStatus.InReview);
    }

    [Fact]
    public void Approve_records_who_and_when()
    {
        var document = Requested();
        document.Upload(File(), Bia, Now);

        document.Approve(Jane, Now.AddHours(1));

        document.Status.Should().Be(DocumentStatus.Approved);
        var version = document.Versions.Single();
        version.ReviewedByName.Should().Be("Jane Analyst");
        version.ReviewedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Reject_needs_a_reason_and_asks_for_a_new_version()
    {
        var document = Requested();
        document.Upload(File(), Bia, Now);

        var withoutReason = () => document.Reject(Jane, " ", Now);
        withoutReason.Should().Throw<ArgumentException>();

        document.Reject(Jane, "Documento vencido", Now.AddHours(1));
        document.Status.Should().Be(DocumentStatus.Rejected);
        document.Versions.Single().ReviewReason.Should().Be("Documento vencido");

        document.Upload(File("licenca-nova.pdf"), Bia, Now.AddDays(1));
        document.Status.Should().Be(DocumentStatus.InReview);
        document.Versions.Select(v => (v.Number, v.Status)).Should().Equal((1, DocumentStatus.Rejected), (2, DocumentStatus.InReview));
    }

    [Fact]
    public void Only_a_version_in_review_can_be_reviewed()
    {
        var document = Requested();

        var approveEmpty = () => document.Approve(Jane, Now);
        approveEmpty.Should().Throw<ProcessRuleException>();

        document.Upload(File(), Bia, Now);
        document.Approve(Jane, Now);
        var approveAgain = () => document.Approve(Jane, Now);
        approveAgain.Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void A_new_version_after_approval_goes_back_to_review()
    {
        var document = Requested();
        document.Upload(File(), Bia, Now);
        document.Approve(Jane, Now);

        document.Upload(File("licenca-2027.pdf"), Bia, Now.AddDays(30));

        document.Status.Should().Be(DocumentStatus.InReview);
    }

    [Fact]
    public void Deliverables_are_published_without_review()
    {
        var document = ProcessDocument.Deliverable(ProcessId, StepId, "Licença de operação", 0);
        document.Status.Should().Be(DocumentStatus.AwaitingUpload);

        document.Upload(File(), Jane, Now);

        document.Status.Should().Be(DocumentStatus.Published);
        var review = () => document.Approve(Jane, Now);
        review.Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void Extras_need_a_description()
    {
        var blank = () => ProcessDocument.Extra(ProcessId, " ", Bia, Now);
        blank.Should().Throw<ArgumentException>();

        var extra = ProcessDocument.Extra(ProcessId, "  Mapa da unidade ", Bia, Now);

        extra.Kind.Should().Be(DocumentKind.Extra);
        extra.Name.Should().Be("Mapa da unidade");
        extra.StepId.Should().BeNull();
        extra.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void Pending_means_required_and_not_approved()
    {
        var awaiting = Requested();
        var inReview = Requested();
        inReview.Upload(File(), Bia, Now);
        var approved = Requested();
        approved.Upload(File(), Bia, Now);
        approved.Approve(Jane, Now);
        var optional = Requested(required: false);

        awaiting.IsPending.Should().BeTrue();
        inReview.IsPending.Should().BeTrue();
        approved.IsPending.Should().BeFalse();
        optional.IsPending.Should().BeFalse();
    }
}
