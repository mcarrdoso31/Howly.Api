using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

public class ProcessTypeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public static StepTemplateDetails Step(string name = "Coleta de documentos", StepOwner owner = StepOwner.Customer) =>
        new(name, null, owner, null, 5, false, "Procuração assinada", [], []);

    private static ProcessType NewType(params StepTemplateDetails[] steps) =>
        ProcessType.Create("Emissão de MTR", "Manifesto de transporte de resíduos", steps.Length > 0 ? steps : [Step()], Now);

    [Fact]
    public void Create_keeps_the_steps_in_order_and_starts_active()
    {
        var type = NewType(Step("Coleta"), Step("Protocolo", StepOwner.Staff), Step("Análise", StepOwner.Agency) with { AgencyName = "IBAMA" });

        type.Name.Should().Be("Emissão de MTR");
        type.IsActive.Should().BeTrue();
        type.CreatedAt.Should().Be(Now);
        type.Steps.Select(s => s.Name).Should().Equal("Coleta", "Protocolo", "Análise");
        type.Steps[2].AgencyName.Should().Be("IBAMA");
        type.Steps[0].DurationDays.Should().Be(5);
    }

    [Fact]
    public void Agency_name_is_kept_only_for_agency_steps()
    {
        var type = NewType(Step("Protocolo", StepOwner.Staff) with { AgencyName = "IBAMA" });

        type.Steps[0].AgencyName.Should().BeNull();
    }

    [Fact]
    public void Blank_texts_become_null_and_names_are_trimmed()
    {
        var type = NewType(new StepTemplateDetails("  Coleta ", "  ", StepOwner.Customer, null, null, true, "  Protocolo ", [], []));

        var step = type.Steps[0];
        step.Name.Should().Be("Coleta");
        step.Description.Should().BeNull();
        step.Notes.Should().Be("Protocolo");
        step.IsOptional.Should().BeTrue();
    }

    [Fact]
    public void Update_replaces_everything_and_records_the_time()
    {
        var type = NewType(Step("Coleta"));

        type.Update("MTR", null, [Step("Protocolo", StepOwner.Staff), Step("Emissão", StepOwner.Agency) with { AgencyName = "IBAMA" }], Now.AddDays(1));

        type.Name.Should().Be("MTR");
        type.Description.Should().BeNull();
        type.Steps.Select(s => s.Name).Should().Equal("Protocolo", "Emissão");
        type.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Deactivate_and_reactivate()
    {
        var type = NewType();

        type.Deactivate();
        type.IsActive.Should().BeFalse();

        type.Reactivate();
        type.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Validate_accepts_a_valid_type() => ProcessType.Validate("MTR", null, [Step()]).Should().BeEmpty();

    [Fact]
    public void Validate_requires_a_name() => ProcessType.Validate(" ", null, [Step()]).Should().ContainKey("name");

    [Fact]
    public void Validate_requires_steps() => ProcessType.Validate("MTR", null, []).Should().ContainKey("steps");

    [Fact]
    public void Validate_limits_the_number_of_steps() =>
        ProcessType.Validate("MTR", null, Enumerable.Range(0, ProcessType.MaxSteps + 1).Select(i => Step($"Etapa {i}")).ToList())
            .Should().ContainKey("steps");

    [Theory]
    [InlineData("name")]
    [InlineData("durationDays")]
    [InlineData("notes")]
    [InlineData("agencyName")]
    public void Validate_points_to_the_invalid_field_of_the_step(string field)
    {
        var step = field switch
        {
            "name" => Step(" "),
            "durationDays" => Step() with { DurationDays = 0 },
            "notes" => Step() with { Notes = new string('x', ProcessType.MaxNotesLength + 1) },
            _ => Step("Análise", StepOwner.Agency) with { AgencyName = new string('x', ProcessType.MaxNameLength + 1) },
        };

        ProcessType.Validate("MTR", null, [Step("Abertura"), step]).Should().ContainKey($"steps[1].{field}").And.HaveCount(1);
    }

    [Fact]
    public void Validate_rejects_an_unknown_owner() =>
        ProcessType.Validate("MTR", null, [Step() with { Owner = (StepOwner)42 }]).Should().ContainKey("steps[0].owner");

    // ---- documents and deliverables ----

    [Fact]
    public void Steps_keep_the_requested_documents_and_deliverables_in_order()
    {
        var type = NewType(Step() with
        {
            Documents = [new(" Licença de operação de máquinas agrícolas ", true), new("CNH do operador", false)],
            Deliverables = [" Protocolo ", "Guia de pagamento"],
        });

        type.Steps[0].Documents.Select(d => (d.Name, d.IsRequired))
            .Should().Equal(("Licença de operação de máquinas agrícolas", true), ("CNH do operador", false));
        type.Steps[0].Deliverables.Should().Equal("Protocolo", "Guia de pagamento");
    }

    [Theory]
    [InlineData("documents")]
    [InlineData("deliverables")]
    public void Validate_requires_names_on_documents_and_deliverables(string list)
    {
        var step = list == "documents" ? Step() with { Documents = [new(" ", true)] } : Step() with { Deliverables = [""] };

        ProcessType.Validate("MTR", null, [step]).Should().ContainKey($"steps[0].{list}[0]");
    }

    [Fact]
    public void Validate_rejects_repeated_document_names_in_a_step() =>
        ProcessType.Validate("MTR", null, [Step() with { Documents = [new("CNH", true), new("cnh", false)] }])
            .Should().ContainKey("steps[0].documents[1]");
}
