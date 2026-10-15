using System.Net;
using System.Text.Json;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Workers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>Pausing with who the process waits on and when it is expected back, and the reminder on that day.</summary>
public class PauseEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"3{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Scenario(HttpClient Client, string Staff, string Customer, Guid ProcessId, Guid AgencyId);

    /// <summary>A consultancy of its own (the summary counts are this test's only), a customer with portal access and a process.</summary>
    private async Task<Scenario> ScenarioAsync()
    {
        var slug = $"pause-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Pausas");
        var client = factory.CreatePortalClient(slug);
        var analyst = NewUsername("analyst");
        await factory.CreateUserInAsync(slug, analyst, Password, "Jane Analyst", Roles.Analyst);
        var staff = await client.LoginInternalAsync(analyst);

        var agency = await (await client.SendAsync(HttpMethod.Post, "/api/agencies", staff, new { acronym = "IAT", name = "Instituto Água e Terra", sphere = "State", state = "PR" })).JsonAsync();
        var contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var cnpj = NewCnpj();
        var customer = await (await client.SendAsync(HttpMethod.Post, "/api/customers", staff, new { document = cnpj, companyName = "Beagle", contacts })).JsonAsync();
        var temporary = customer.GetProperty("temporaryPassword").GetString()!;
        var pending = (await client.LoginCustomerAsync(cnpj, temporary)).GetProperty("accessToken").GetString();
        var changed = await (await client.SendAsync(HttpMethod.Post, "/api/auth/change-password", pending, new { currentPassword = temporary, newPassword = NewPassword })).JsonAsync();
        await client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", changed.GetProperty("accessToken").GetString(), new { contacts });
        var customerToken = (await client.LoginCustomerAsync(cnpj, NewPassword)).GetProperty("accessToken").GetString()!;

        var type = await (await client.SendAsync(HttpMethod.Post, "/api/process-types", staff,
            new { name = "LO", steps = new object[] { new { name = "Análise", owner = "Agency", durationDays = 90 } } })).JsonAsync();
        var process = await (await client.SendAsync(HttpMethod.Post, "/api/processes", staff,
            new { customerId = customer.GetProperty("id").GetGuid(), processTypeId = type.GetProperty("id").GetGuid(), title = "LO — Curitiba" })).JsonAsync();
        return new Scenario(client, staff, customerToken, process.GetProperty("id").GetGuid(), agency.GetProperty("id").GetGuid());
    }

    private static string Date(DateTimeOffset at) => at.ToString("yyyy-MM-dd");

    [Fact]
    public async Task A_pause_tells_who_it_waits_on_and_when_it_is_expected_back()
    {
        var s = await ScenarioAsync();
        var expected = Date(DateTimeOffset.UtcNow.AddDays(30));

        var paused = await (await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/pause", s.Staff,
            new { reason = "Recesso do IAT", waitingOn = "Agency", agencyId = s.AgencyId, expectedReturnOn = expected })).JsonAsync();

        var pause = paused.GetProperty("pause");
        pause.GetProperty("waitingOn").GetString().Should().Be("Agency");
        pause.GetProperty("waitingOnName").GetString().Should().Be("IAT");
        pause.GetProperty("expectedReturnOn").GetString().Should().Be(expected);

        var seenByCustomer = await (await s.Client.SendAsync(HttpMethod.Get, $"/api/me/processes/{s.ProcessId}", s.Customer)).JsonAsync();
        seenByCustomer.GetProperty("pause").GetProperty("expectedReturnOn").GetString().Should().Be(expected);
        seenByCustomer.GetProperty("statusReason").GetString().Should().Be("Recesso do IAT");

        var resumed = await (await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/resume", s.Staff, new { })).JsonAsync();
        resumed.GetProperty("pause").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("expectedReturnOn")]
    [InlineData("waitingOnName")]
    [InlineData("agencyId")]
    public async Task Invalid_pause_details_point_to_the_field(string field)
    {
        var s = await ScenarioAsync();
        object body = field switch
        {
            "expectedReturnOn" => new { reason = "Recesso", expectedReturnOn = Date(DateTimeOffset.UtcNow.AddDays(-3)) },
            "waitingOnName" => new { reason = "Laboratório", waitingOn = "Other", waitingOnName = " " },
            _ => new { reason = "Órgão", waitingOn = "Agency", agencyId = Guid.NewGuid() },
        };

        var response = await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/pause", s.Staff, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task On_the_expected_day_the_staff_are_reminded_and_the_customer_is_not()
    {
        var s = await ScenarioAsync();
        await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/pause", s.Staff,
            new { reason = "Recesso do IAT", waitingOn = "Agency", agencyId = s.AgencyId, expectedReturnOn = Date(DateTimeOffset.UtcNow.AddDays(5)) });
        // The analyst opened the process, so their own events never ring their bell; the reminder comes from the system.

        await factory.Services.GetRequiredService<OverdueStepsRunner>().RunOnceAsync(DateTimeOffset.UtcNow.AddDays(6));

        var staffBell = await (await s.Client.SendAsync(HttpMethod.Get, "/api/me/notifications", s.Staff)).JsonAsync();
        staffBell.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()).Should().Contain("PauseReturnDue");
        var customerBell = await (await s.Client.SendAsync(HttpMethod.Get, "/api/me/notifications", s.Customer)).JsonAsync();
        customerBell.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()).Should().NotContain("PauseReturnDue");
        var customerView = await (await s.Client.SendAsync(HttpMethod.Get, $"/api/me/processes/{s.ProcessId}", s.Customer)).JsonAsync();
        customerView.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()).Should().NotContain("PauseReturnDue");
        var process = await (await s.Client.SendAsync(HttpMethod.Get, $"/api/processes/{s.ProcessId}", s.Staff)).JsonAsync();
        process.GetProperty("status").GetString().Should().Be("Paused", "the staff decide when to resume");
    }

    [Fact]
    public async Task The_summary_counts_the_paused_ones()
    {
        var s = await ScenarioAsync();
        await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/pause", s.Staff, new { reason = "Recesso" });

        var summary = await (await s.Client.SendAsync(HttpMethod.Get, "/api/processes/summary", s.Staff)).JsonAsync();

        summary.GetProperty("paused").GetInt32().Should().Be(1);
        summary.GetProperty("open").GetInt32().Should().Be(0, "open counts the processes in progress");
        summary.GetProperty("pausedPastReturn").GetInt32().Should().Be(0);
    }
}
