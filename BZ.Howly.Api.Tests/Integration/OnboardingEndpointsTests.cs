using System.Net;
using System.Text.Json;
using BZ.Howly.Api.Core.Identity;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>The first steps of a new consultancy, each one marked done by what already exists.</summary>
public class OnboardingEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"5{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private static bool[] Steps(JsonElement onboarding) =>
    [
        onboarding.GetProperty("companyProfile").GetBoolean(),
        onboarding.GetProperty("agencies").GetBoolean(),
        onboarding.GetProperty("processTypes").GetBoolean(),
        onboarding.GetProperty("customers").GetBoolean(),
        onboarding.GetProperty("processes").GetBoolean(),
        onboarding.GetProperty("team").GetBoolean(),
    ];

    [Fact]
    public async Task Each_step_is_done_once_what_it_asks_for_exists()
    {
        // A consultancy of its own: a new one has nothing yet.
        var slug = $"onb-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Nova");
        var client = factory.CreatePortalClient(slug);
        var adminName = NewUsername("admin");
        await factory.CreateUserInAsync(slug, adminName, Password, "Ada Admin", Roles.Admin);
        var admin = await client.LoginInternalAsync(adminName);
        async Task<bool[]> Now() => Steps(await (await client.SendAsync(HttpMethod.Get, "/api/onboarding", admin)).JsonAsync());

        (await Now()).Should().Equal(false, false, false, false, false, false);

        await client.SendAsync(HttpMethod.Put, "/api/tenant/profile", admin, new
        {
            name = "Nova Consultoria", legalName = "Nova Consultoria Ambiental Ltda", cnpj = "64.899.707/0001-64", email = "contato@nova.example",
            phone = (string?)null, postalCode = (string?)null, street = (string?)null, city = (string?)null, state = (string?)null, timeZone = "America/Sao_Paulo",
        });
        await client.SendAsync(HttpMethod.Post, "/api/agencies", admin, new { acronym = "CETESB", name = "Companhia Ambiental do Estado de São Paulo", sphere = "State", state = "SP" });
        var type = await (await client.SendAsync(HttpMethod.Post, "/api/process-types", admin,
            new { name = "LO", steps = new object[] { new { name = "Análise", owner = "Agency" } } })).JsonAsync();
        var customer = await (await client.SendAsync(HttpMethod.Post, "/api/customers", admin, new
        {
            document = NewCnpj(),
            companyName = "Beagle",
            contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } },
        })).JsonAsync();

        (await Now()).Should().Equal(true, true, true, true, false, false);

        await client.SendAsync(HttpMethod.Post, "/api/processes", admin,
            new { customerId = customer.GetProperty("id").GetGuid(), processTypeId = type.GetProperty("id").GetGuid(), title = "LO — Jundiaí" });
        await factory.CreateUserInAsync(slug, NewUsername("ana"), Password, "Ana Analyst", Roles.Analyst);

        (await Now()).Should().Equal(true, true, true, true, true, true);
    }

    [Fact]
    public async Task Customers_have_no_onboarding()
    {
        var response = await factory.CreateClient().GetAsync("/api/onboarding");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
