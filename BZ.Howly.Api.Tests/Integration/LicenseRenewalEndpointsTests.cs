using System.Net;
using System.Text.Json;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Workers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>The validity of each process's license, the reminder to renew it and opening the renewal.</summary>
public class LicenseRenewalEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"4{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Scenario(HttpClient Client, string Staff, string Customer, Guid ProcessId, Guid CustomerId, Guid TypeId);

    /// <summary>A consultancy of its own (the summary counts are this test's only) and a completed license process.</summary>
    private async Task<Scenario> ScenarioAsync()
    {
        var slug = $"lic-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Licenças");
        var client = factory.CreatePortalClient(slug);
        var analyst = NewUsername("analyst");
        await factory.CreateUserInAsync(slug, analyst, Password, "Jane Analyst", Roles.Analyst);
        var staff = await client.LoginInternalAsync(analyst);

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
        var customerId = customer.GetProperty("id").GetGuid();
        var typeId = type.GetProperty("id").GetGuid();
        var process = await (await client.SendAsync(HttpMethod.Post, "/api/processes", staff, new { customerId, processTypeId = typeId, title = "LO — Jundiaí" })).JsonAsync();
        var processId = process.GetProperty("id").GetGuid();
        await client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/advance", staff, new { });
        return new Scenario(client, staff, customerToken, processId, customerId, typeId);
    }

    private static string Date(DateTimeOffset at) => at.ToString("yyyy-MM-dd");

    [Fact]
    public async Task The_staff_tell_the_validity_and_the_customer_sees_it()
    {
        var s = await ScenarioAsync();
        var expires = Date(DateTimeOffset.UtcNow.AddYears(4));
        var starts = Date(DateTimeOffset.UtcNow.AddYears(4).AddDays(-120));

        var response = await s.Client.SendAsync(HttpMethod.Put, $"/api/processes/{s.ProcessId}/license", s.Staff, new { expiresOn = expires, renewalStartsOn = starts });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var license = (await response.JsonAsync()).GetProperty("license");
        license.GetProperty("expiresOn").GetString().Should().Be(expires);
        license.GetProperty("renewalStartsOn").GetString().Should().Be(starts);
        license.GetProperty("renewalProcessId").ValueKind.Should().Be(JsonValueKind.Null);

        var seenByCustomer = await (await s.Client.SendAsync(HttpMethod.Get, $"/api/me/processes/{s.ProcessId}", s.Customer)).JsonAsync();
        seenByCustomer.GetProperty("license").GetProperty("renewalStartsOn").GetString().Should().Be(starts);
        seenByCustomer.GetProperty("events").EnumerateArray().Last().GetProperty("type").GetString().Should().Be("LicenseChanged");
    }

    [Fact]
    public async Task The_renewal_cannot_start_after_the_license_expires()
    {
        var s = await ScenarioAsync();

        var response = await s.Client.SendAsync(HttpMethod.Put, $"/api/processes/{s.ProcessId}/license", s.Staff,
            new { expiresOn = Date(DateTimeOffset.UtcNow.AddYears(1)), renewalStartsOn = Date(DateTimeOffset.UtcNow.AddYears(2)) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("renewalStartsOn", out _).Should().BeTrue();
    }

    [Fact]
    public async Task On_the_renewal_start_the_staff_are_reminded_and_see_it_on_the_home_page()
    {
        var s = await ScenarioAsync();
        await s.Client.SendAsync(HttpMethod.Put, $"/api/processes/{s.ProcessId}/license", s.Staff,
            new { expiresOn = Date(DateTimeOffset.UtcNow.AddDays(130)), renewalStartsOn = Date(DateTimeOffset.UtcNow.AddDays(10)) });

        var before = await (await s.Client.SendAsync(HttpMethod.Get, "/api/processes/summary", s.Staff)).JsonAsync();
        before.GetProperty("renewalsDue").GetInt32().Should().Be(0);

        await factory.Services.GetRequiredService<OverdueStepsRunner>().RunOnceAsync(DateTimeOffset.UtcNow.AddDays(11));

        var staffBell = await (await s.Client.SendAsync(HttpMethod.Get, "/api/me/notifications", s.Staff)).JsonAsync();
        staffBell.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()).Should().Contain("RenewalDue");
        var customerView = await (await s.Client.SendAsync(HttpMethod.Get, $"/api/me/processes/{s.ProcessId}", s.Customer)).JsonAsync();
        customerView.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()).Should().NotContain("RenewalDue");
    }

    [Fact]
    public async Task Opening_the_renewal_creates_the_new_process_and_takes_it_off_the_list()
    {
        var s = await ScenarioAsync();
        await s.Client.SendAsync(HttpMethod.Put, $"/api/processes/{s.ProcessId}/license", s.Staff,
            new { expiresOn = Date(DateTimeOffset.UtcNow.AddDays(100)), renewalStartsOn = Date(DateTimeOffset.UtcNow.AddDays(-1)) });

        var due = await (await s.Client.SendAsync(HttpMethod.Get, "/api/processes/summary", s.Staff)).JsonAsync();
        due.GetProperty("renewalsDue").GetInt32().Should().Be(1);
        var listed = await (await s.Client.SendAsync(HttpMethod.Get, "/api/processes?renewalDue=true", s.Staff)).JsonAsync();
        listed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(s.ProcessId);
        listed.GetProperty("items")[0].GetProperty("renewalStartsOn").GetString().Should().NotBeNull();

        var opened = await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/renewal", s.Staff, new { title = "Renovação — LO — Jundiaí" });

        opened.StatusCode.Should().Be(HttpStatusCode.Created);
        var renewal = await opened.JsonAsync();
        renewal.GetProperty("title").GetString().Should().Be("Renovação — LO — Jundiaí");
        renewal.GetProperty("customer").GetProperty("id").GetGuid().Should().Be(s.CustomerId);
        renewal.GetProperty("processTypeId").GetGuid().Should().Be(s.TypeId);
        renewal.GetProperty("status").GetString().Should().Be("InProgress");

        var original = await (await s.Client.SendAsync(HttpMethod.Get, $"/api/processes/{s.ProcessId}", s.Staff)).JsonAsync();
        original.GetProperty("license").GetProperty("renewalProcessId").GetGuid().Should().Be(renewal.GetProperty("id").GetGuid());
        original.GetProperty("events").EnumerateArray().Last().GetProperty("type").GetString().Should().Be("RenewalOpened");
        var after = await (await s.Client.SendAsync(HttpMethod.Get, "/api/processes/summary", s.Staff)).JsonAsync();
        after.GetProperty("renewalsDue").GetInt32().Should().Be(0);

        var again = await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/renewal", s.Staff, new { title = "Outra" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Customers_cannot_change_the_license()
    {
        var s = await ScenarioAsync();

        var response = await s.Client.SendAsync(HttpMethod.Put, $"/api/processes/{s.ProcessId}/license", s.Customer, new { expiresOn = "2030-01-01" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
