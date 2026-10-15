using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>
/// Two people changing the same process at the same moment: the one who loses gets a conflict, and nothing is left
/// half done (no renewal without its link, no finished parent with an open subprocess).
/// </summary>
public class ConcurrentChangesConsistencyTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"6{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Scenario(string Slug, HttpClient Client, string Staff, Guid StaffId, Guid CustomerId, Guid TypeId);

    private async Task<Scenario> ScenarioAsync()
    {
        var slug = $"cc-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Concorrência");
        var client = factory.CreatePortalClient(slug);
        var analyst = NewUsername("analyst");
        var staffId = await factory.CreateUserInAsync(slug, analyst, Password, "Jane Analyst", Roles.Analyst);
        var staff = await client.LoginInternalAsync(analyst);

        var contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var customer = await (await client.SendAsync(HttpMethod.Post, "/api/customers", staff, new { document = NewCnpj(), companyName = "Beagle", contacts })).JsonAsync();
        var type = await (await client.SendAsync(HttpMethod.Post, "/api/process-types", staff,
            new { name = "LO", steps = new object[] { new { name = "Análise", owner = "Staff" } } })).JsonAsync();
        return new Scenario(slug, client, staff, staffId, customer.GetProperty("id").GetGuid(), type.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> OpenAsync(Scenario s, string title, Guid? parentId = null) =>
        (await (await s.Client.SendAsync(HttpMethod.Post, "/api/processes", s.Staff,
            new { customerId = s.CustomerId, processTypeId = s.TypeId, title, parentId })).JsonAsync()).GetProperty("id").GetGuid();

    private static async Task<int> CountProcessesAsync(Scenario s) =>
        (await (await s.Client.SendAsync(HttpMethod.Get, $"/api/processes?customerId={s.CustomerId}", s.Staff)).JsonAsync())
            .GetProperty("totalCount").GetInt32();

    [Fact]
    public async Task A_renewal_opened_on_a_process_changed_meanwhile_leaves_no_orphan()
    {
        var s = await ScenarioAsync();
        var processId = await OpenAsync(s, "LO — Jundiaí");
        await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/advance", s.Staff, new { });

        // Someone has the process loaded (an older version) while another person tells the license validity.
        using var scope = factory.CreateTenantScope(s.Slug);
        await scope.ServiceProvider.GetRequiredService<IProcessRepository>().FindByIdAsync(processId);
        (await s.Client.SendAsync(HttpMethod.Put, $"/api/processes/{processId}/license", s.Staff, new { expiresOn = "2031-10-15", renewalStartsOn = (string?)null }))
            .EnsureSuccessStatusCode();

        var opening = () => scope.ServiceProvider.GetRequiredService<IProcessService>()
            .OpenRenewalAsync(processId, new OpenRenewalRequest("Renovação — LO Jundiaí"), s.StaffId);

        await opening.Should().ThrowAsync<ConflictException>();
        (await CountProcessesAsync(s)).Should().Be(1, "the renewal is saved together with its link, or not at all");
    }

    [Fact]
    public async Task A_subprocess_opened_meanwhile_makes_cancelling_the_parent_conflict()
    {
        var s = await ScenarioAsync();
        var parentId = await OpenAsync(s, "LO — Jundiaí");

        // Someone has the parent loaded (and counted no open subprocess) while another person opens one.
        using var scope = factory.CreateTenantScope(s.Slug);
        var repository = scope.ServiceProvider.GetRequiredService<IProcessRepository>();
        var parent = (await repository.FindByIdAsync(parentId))!;
        await OpenAsync(s, "Outorga", parentId);

        parent.Cancel(new Actor(s.StaffId, "Jane Analyst"), "Cliente desistiu.", DateTimeOffset.UtcNow, openSubprocesses: 0);
        var saving = () => repository.SaveChangesAsync();

        await saving.Should().ThrowAsync<ConflictException>();
        var stored = await (await s.Client.SendAsync(HttpMethod.Get, $"/api/processes/{parentId}", s.Staff)).JsonAsync();
        stored.GetProperty("status").GetString().Should().Be("InProgress");
        stored.GetProperty("children").EnumerateArray().Should().ContainSingle(c => c.GetProperty("status").GetString() == "InProgress");
    }

    [Fact]
    public async Task The_renewal_still_opens_and_links_when_nobody_else_changed_the_process()
    {
        var s = await ScenarioAsync();
        var processId = await OpenAsync(s, "LO — Jundiaí");
        await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/advance", s.Staff, new { });

        var renewal = await (await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/renewal", s.Staff, new { title = "Renovação" })).JsonAsync();

        var original = await (await s.Client.SendAsync(HttpMethod.Get, $"/api/processes/{processId}", s.Staff)).JsonAsync();
        original.GetProperty("license").GetProperty("renewalProcessId").GetGuid().Should().Be(renewal.GetProperty("id").GetGuid());
        (await CountProcessesAsync(s)).Should().Be(2);
        original.GetProperty("events").EnumerateArray().Last().GetProperty("type").GetString().Should().Be("RenewalOpened");
    }
}
