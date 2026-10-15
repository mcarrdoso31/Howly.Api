using BZ.Howly.Api.Core.Processes;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

/// <summary>
/// Each process keeps the validity of its own license and when its renewal starts (rules differ per license and per
/// customer); on that day the staff are told, and they open the renewal.
/// </summary>
public class LicenseRenewalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);
    private static readonly Actor Jane = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Jane Analyst");

    private static Process Completed()
    {
        var process = Process.Open(Guid.NewGuid(), ProcessType.Create("LO", null,
        [
            new("Análise", null, StepOwner.Agency, "CETESB", 90, false, null, [], []),
        ], Now), "LO — Jundiaí", Jane.Id, null, Jane, Now);
        process.Advance(Jane, null, Now);
        return process;
    }

    [Fact]
    public void The_validity_and_the_renewal_start_are_kept_and_told_on_the_timeline()
    {
        var process = Completed();

        process.SetLicense(new DateOnly(2030, 10, 15), new DateOnly(2030, 6, 17), Jane, Now);

        process.LicenseExpiresOn.Should().Be(new DateOnly(2030, 10, 15));
        process.RenewalStartsOn.Should().Be(new DateOnly(2030, 6, 17));
        var told = process.Events.Last();
        told.Type.Should().Be(ProcessEventType.LicenseChanged);
        told.Details.Should().Be("2030-10-15");
        told.IsVisibleToCustomer.Should().BeTrue();
    }

    [Fact]
    public void Both_dates_are_optional_but_the_renewal_cannot_start_after_the_license_expires()
    {
        var process = Completed();

        process.SetLicense(new DateOnly(2030, 10, 15), null, Jane, Now);
        process.RenewalStartsOn.Should().BeNull();
        process.SetLicense(null, new DateOnly(2030, 6, 17), Jane, Now);
        process.LicenseExpiresOn.Should().BeNull();

        var act = () => process.SetLicense(new DateOnly(2030, 10, 15), new DateOnly(2030, 10, 16), Jane, Now);
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("renewalStartsOn");
    }

    [Fact]
    public void A_cancelled_process_has_no_license()
    {
        var process = Process.Open(Guid.NewGuid(), ProcessType.Create("LO", null,
        [
            new("Análise", null, StepOwner.Agency, "CETESB", 90, false, null, [], []),
        ], Now), "LO", Jane.Id, null, Jane, Now);
        process.Cancel(Jane, "Desistiu", Now);

        var act = () => process.SetLicense(new DateOnly(2030, 10, 15), null, Jane, Now);

        act.Should().Throw<ProcessRuleException>();
    }

    [Fact]
    public void The_staff_are_told_once_when_the_renewal_start_arrives()
    {
        var process = Completed();
        process.SetLicense(new DateOnly(2027, 2, 1), Today, Jane, Now);

        process.MarkRenewalDue(Today.AddDays(-1), Now).Should().BeFalse();
        process.MarkRenewalDue(Today, Now).Should().BeTrue();
        process.MarkRenewalDue(Today.AddDays(1), Now).Should().BeFalse();

        var due = process.Events.Last();
        due.Type.Should().Be(ProcessEventType.RenewalDue);
        due.IsVisibleToCustomer.Should().BeFalse();
    }

    [Fact]
    public void Changing_the_renewal_start_tells_again_on_the_new_day()
    {
        var process = Completed();
        process.SetLicense(null, Today, Jane, Now);
        process.MarkRenewalDue(Today, Now);

        process.SetLicense(null, Today.AddDays(10), Jane, Now);

        process.MarkRenewalDue(Today.AddDays(10), Now).Should().BeTrue();
    }

    [Fact]
    public void Opening_the_renewal_links_it_and_stops_the_reminders()
    {
        var process = Completed();
        process.SetLicense(null, Today, Jane, Now);
        var renewal = Guid.NewGuid();

        process.LinkRenewal(renewal, "Renovação — LO — Jundiaí", Jane, Now);

        process.RenewalProcessId.Should().Be(renewal);
        process.Events.Last().Type.Should().Be(ProcessEventType.RenewalOpened);
        process.MarkRenewalDue(Today, Now).Should().BeFalse();
        var again = () => process.LinkRenewal(Guid.NewGuid(), "Outra", Jane, Now);
        again.Should().Throw<ProcessRuleException>();
    }
}
