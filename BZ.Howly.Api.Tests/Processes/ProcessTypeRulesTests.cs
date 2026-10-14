using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

public class ProcessTypeRulesTests
{
    private static StepTemplateDetails Step(string? name = "Coleta", StepOwner? owner = StepOwner.Customer, string? agencyName = null) =>
        new(name, null, owner, agencyName, 5, false, null, null, [], []);

    [Fact]
    public void A_missing_owner_is_an_error_instead_of_the_customer() =>
        ProcessType.Validate("MTR", null, [Step(owner: null)]).Should().ContainKey("steps[0].owner");

    [Fact]
    public void Agency_steps_may_leave_the_agency_for_each_process()
    {
        ProcessType.Validate("MTR", null, [Step("Análise", StepOwner.Agency)]).Should().BeEmpty();
        ProcessType.Validate("MTR", null, [Step("Análise", StepOwner.Agency, new string('x', 121))]).Should().ContainKey("steps[0].agencyName");
        ProcessType.Validate("MTR", null, [Step("Análise", StepOwner.Agency, "IBAMA")]).Should().BeEmpty();
    }

    [Fact]
    public void Step_names_do_not_repeat_in_a_type() =>
        ProcessType.Validate("MTR", null, [Step("Coleta"), Step(" coleta ")]).Should().ContainKey("steps[1].name");

    [Fact]
    public void Null_items_in_the_request_are_field_errors()
    {
        var request = new ProcessTypeRequest("MTR", null,
        [
            null!,
            new StepTemplateRequest("Coleta", null, StepOwner.Customer, null, 5, false, null, null, [null!], [null!]),
        ]);

        var errors = ProcessType.Validate(request.Name, request.Description, request.StepDetails());

        errors.Should().ContainKey("steps[0].name");
        errors.Should().ContainKey("steps[1].documents[0]");
        errors.Should().ContainKey("steps[1].deliverables[0]");
    }
}
