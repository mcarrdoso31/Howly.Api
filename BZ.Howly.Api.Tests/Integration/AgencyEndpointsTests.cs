using System.Net;
using System.Text.Json;
using BZ.Howly.Api.Core.Identity;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>The agencies catalog, the default agency of a step and the agency of each process step.</summary>
public class AgencyEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"4{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Portal(HttpClient Client, string Analyst);

    /// <summary>A consultancy of its own: its catalog starts empty.</summary>
    private async Task<Portal> PortalAsync()
    {
        var slug = $"ag-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Órgãos");
        var client = factory.CreatePortalClient(slug);
        var analyst = NewUsername("analyst");
        await factory.CreateUserInAsync(slug, analyst, Password, "Caio Analyst", Roles.Analyst);
        return new Portal(client, await client.LoginInternalAsync(analyst));
    }

    private static async Task<JsonElement> CreateAgencyAsync(Portal p, string acronym, string sphere = "State", string? state = "SP")
    {
        var response = await p.Client.SendAsync(HttpMethod.Post, "/api/agencies", p.Analyst,
            new { acronym, name = $"Agência {acronym}", sphere, state, city = (string?)null, website = (string?)null, protocolSystem = "SEI" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.JsonAsync();
    }

    [Fact]
    public async Task Analysts_keep_the_catalog_and_deactivated_agencies_leave_the_list()
    {
        var p = await PortalAsync();
        var cetesb = await CreateAgencyAsync(p, "CETESB");
        var ibama = await CreateAgencyAsync(p, "IBAMA", "Federal", state: null);
        var id = cetesb.GetProperty("id").GetGuid();

        var updated = await (await p.Client.SendAsync(HttpMethod.Put, $"/api/agencies/{id}", p.Analyst,
            new { acronym = "CETESB", name = "Companhia Ambiental do Estado de São Paulo", sphere = "State", state = "sp", protocolSystem = "e-ambiente" })).JsonAsync();
        updated.GetProperty("state").GetString().Should().Be("SP");
        ibama.GetProperty("state").ValueKind.Should().Be(JsonValueKind.Null);

        await p.Client.SendAsync(HttpMethod.Post, $"/api/agencies/{ibama.GetProperty("id").GetGuid()}/deactivate", p.Analyst);
        var active = await (await p.Client.SendAsync(HttpMethod.Get, "/api/agencies", p.Analyst)).JsonAsync();
        var all = await (await p.Client.SendAsync(HttpMethod.Get, "/api/agencies?includeInactive=true", p.Analyst)).JsonAsync();

        active.EnumerateArray().Select(a => a.GetProperty("acronym").GetString()).Should().Equal("CETESB");
        all.EnumerateArray().Select(a => a.GetProperty("acronym").GetString()).Should().Equal("CETESB", "IBAMA");
    }

    [Fact]
    public async Task Invalid_agencies_point_to_the_field()
    {
        var p = await PortalAsync();

        var response = await p.Client.SendAsync(HttpMethod.Post, "/api/agencies", p.Analyst, new { acronym = "SVMA", name = "Secretaria do Verde", sphere = "Municipal", state = "SP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("city", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Customers_do_not_reach_the_catalog() =>
        (await factory.CreateClient().GetAsync("/api/agencies")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task A_process_starts_with_the_default_agency_and_each_process_can_change_it()
    {
        var p = await PortalAsync();
        var cetesb = (await CreateAgencyAsync(p, "CETESB")).GetProperty("id").GetGuid();
        var feam = (await CreateAgencyAsync(p, "FEAM", state: "MG")).GetProperty("id").GetGuid();
        var type = await (await p.Client.SendAsync(HttpMethod.Post, "/api/process-types", p.Analyst, new
        {
            name = "Renovação de LO",
            steps = new object[]
            {
                new { name = "Protocolo", owner = "Staff" },
                new { name = "Análise", owner = "Agency", agencyId = cetesb },
            },
        })).JsonAsync();
        type.GetProperty("steps")[1].GetProperty("agencyId").GetGuid().Should().Be(cetesb);
        type.GetProperty("steps")[1].GetProperty("agencyName").GetString().Should().Be("CETESB");

        var customer = await (await p.Client.SendAsync(HttpMethod.Post, "/api/customers", p.Analyst, new
        {
            cnpj = NewCnpj(),
            companyName = "Beagle",
            contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } },
        })).JsonAsync();
        var process = await (await p.Client.SendAsync(HttpMethod.Post, "/api/processes", p.Analyst, new
        {
            customerId = customer.GetProperty("id").GetGuid(),
            processTypeId = type.GetProperty("id").GetGuid(),
            title = "LO — Uberaba",
        })).JsonAsync();
        var processId = process.GetProperty("id").GetGuid();
        var agencyStep = process.GetProperty("steps")[1];
        agencyStep.GetProperty("agencyName").GetString().Should().Be("CETESB");

        var changed = await p.Client.SendAsync(HttpMethod.Put, $"/api/processes/{processId}/steps/{agencyStep.GetProperty("id").GetGuid()}/agency",
            p.Analyst, new { agencyId = feam });

        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await changed.JsonAsync();
        after.GetProperty("steps")[1].GetProperty("agencyId").GetGuid().Should().Be(feam);
        after.GetProperty("steps")[1].GetProperty("agencyName").GetString().Should().Be("FEAM");
        after.GetProperty("events").EnumerateArray().Last().GetProperty("type").GetString().Should().Be("StepAgencyChanged");
    }

    [Fact]
    public async Task Unknown_or_inactive_agencies_are_refused()
    {
        var p = await PortalAsync();
        var gone = (await CreateAgencyAsync(p, "GONE")).GetProperty("id").GetGuid();
        await p.Client.SendAsync(HttpMethod.Post, $"/api/agencies/{gone}/deactivate", p.Analyst);

        foreach (var agencyId in new[] { Guid.NewGuid(), gone })
        {
            var response = await p.Client.SendAsync(HttpMethod.Post, "/api/process-types", p.Analyst,
                new { name = $"LO {Guid.NewGuid():N}"[..10], steps = new object[] { new { name = "Análise", owner = "Agency", agencyId } } });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.JsonAsync()).GetProperty("errors").TryGetProperty("steps[0].agencyId", out _).Should().BeTrue();
        }
    }
}
