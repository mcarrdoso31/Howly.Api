using BZ.Howly.Api.Core.Agencies;
using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

/// <summary>The agencies a consultancy works with, and the agency of each agency step of a process.</summary>
public class AgencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Actor Jane = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Jane Analyst");

    private static AgencyDetails Details(string acronym = "CETESB", AgencySphere sphere = AgencySphere.State, string? state = "SP", string? city = null) =>
        new(acronym, "Companhia Ambiental do Estado de São Paulo", sphere, state, city, "https://cetesb.sp.gov.br", "e-ambiente");

    [Fact]
    public void An_agency_keeps_its_data_trimmed()
    {
        var agency = Agency.Create(Details(" CETESB "), Now);

        agency.Acronym.Should().Be("CETESB");
        agency.Sphere.Should().Be(AgencySphere.State);
        agency.State.Should().Be("SP");
        agency.IsActive.Should().BeTrue();
        agency.DisplayName.Should().Be("CETESB");
    }

    [Fact]
    public void Federal_agencies_have_no_state_nor_city()
    {
        var agency = Agency.Create(Details("IBAMA", AgencySphere.Federal, "SP", "Santos"), Now);

        agency.State.Should().BeNull();
        agency.City.Should().BeNull();
    }

    [Theory]
    [InlineData("acronym")]
    [InlineData("state")]
    [InlineData("city")]
    public void Validate_points_to_the_missing_field(string field)
    {
        var details = field switch
        {
            "acronym" => Details(" "),
            "state" => Details(state: null),
            _ => Details("SVMA", AgencySphere.Municipal, "SP", city: null),
        };

        Agency.Validate(details).Should().ContainKey(field);
    }

    [Fact]
    public void Deactivated_agencies_can_come_back()
    {
        var agency = Agency.Create(Details(), Now);

        agency.Deactivate(Now);
        agency.IsActive.Should().BeFalse();
        agency.Reactivate(Now);
        agency.IsActive.Should().BeTrue();
    }

    // ---- the agency of a process step ----

    private static Process Open(Guid? defaultAgency = null) => Process.Open(Guid.NewGuid(), ProcessType.Create("LO", null,
    [
        new("Protocolo", null, StepOwner.Staff, null, 3, false, null, null, [], []),
        new("Análise", null, StepOwner.Agency, "CETESB", 30, false, null, null, [], [], defaultAgency),
    ], Now), "LO", Jane.Id, null, Jane, Now);

    [Fact]
    public void The_process_copies_the_default_agency_of_the_type()
    {
        var agencyId = Guid.NewGuid();

        var step = Open(agencyId).Steps[1];

        step.AgencyId.Should().Be(agencyId);
        step.AgencyName.Should().Be("CETESB");
    }

    [Fact]
    public void Agency_steps_need_no_agency_up_front() =>
        ProcessType.Validate("LO", null, [new("Análise", null, StepOwner.Agency, null, 30, false, null, null, [], [])]).Should().BeEmpty();

    [Fact]
    public void The_staff_change_the_agency_of_a_step_not_finished_yet()
    {
        var process = Open();
        var feam = Guid.NewGuid();

        process.SetStepAgency(process.Steps[1].Id, feam, "FEAM", Jane, Now);

        process.Steps[1].AgencyId.Should().Be(feam);
        process.Steps[1].AgencyName.Should().Be("FEAM");
        var changed = process.Events.Last();
        changed.Type.Should().Be(ProcessEventType.StepAgencyChanged);
        changed.StepName.Should().Be("Análise");
        changed.Details.Should().Be("FEAM");
    }

    [Fact]
    public void Only_open_agency_steps_change_agency()
    {
        var process = Open();
        var staffStep = process.Steps[0].Id;

        process.Invoking(p => p.SetStepAgency(staffStep, Guid.NewGuid(), "FEAM", Jane, Now)).Should().Throw<ProcessRuleException>("not an agency step");

        process.Advance(Jane, null, Now);
        process.Advance(Jane, null, Now); // completed: every step finished
        process.Invoking(p => p.SetStepAgency(p.Steps[1].Id, Guid.NewGuid(), "FEAM", Jane, Now)).Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void A_paused_process_keeps_its_agencies()
    {
        var process = Open();
        process.Pause(Jane, "Pausa", Now);

        process.Invoking(p => p.SetStepAgency(p.Steps[1].Id, Guid.NewGuid(), "FEAM", Jane, Now)).Should().Throw<ProcessRuleException>();
    }
}
