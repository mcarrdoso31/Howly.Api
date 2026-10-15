using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

/// <summary>Editing a type keeps the identity of the steps it still has.</summary>
public class ProcessTypeStepIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static StepTemplateDetails Step(string name, Guid? id = null, string? notes = null) =>
        new(name, null, StepOwner.Staff, null, 5, false, notes, [], [], Id: id);

    [Fact]
    public void Steps_given_with_their_id_keep_it_and_take_the_new_data()
    {
        var type = ProcessType.Create("LO", null, [Step("Coleta"), Step("Protocolo")], Now);
        var (coleta, protocolo) = (type.Steps[0].Id, type.Steps[1].Id);

        type.Update("LO", null, [Step("Protocolo no órgão", protocolo, "Levar via original"), Step("Nova"), Step("Coleta", coleta)], Now);

        type.Steps.Select(s => s.Name).Should().Equal("Protocolo no órgão", "Nova", "Coleta");
        type.Steps[0].Id.Should().Be(protocolo);
        type.Steps[0].Notes.Should().Be("Levar via original");
        type.Steps[2].Id.Should().Be(coleta);
        type.Steps[1].Id.Should().NotBe(coleta).And.NotBe(protocolo);
    }

    [Fact]
    public void Steps_left_out_are_removed_and_unknown_or_repeated_ids_make_new_steps()
    {
        var type = ProcessType.Create("LO", null, [Step("Coleta"), Step("Protocolo")], Now);
        var coleta = type.Steps[0].Id;

        type.Update("LO", null, [Step("Coleta", coleta), Step("Cópia", coleta), Step("Estranha", Guid.NewGuid())], Now);

        type.Steps.Should().HaveCount(3);
        type.Steps[0].Id.Should().Be(coleta);
        type.Steps.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }
}
