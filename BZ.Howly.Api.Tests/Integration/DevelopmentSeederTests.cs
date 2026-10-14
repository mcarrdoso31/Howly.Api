using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BZ.Howly.Api.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>The development seed leaves a consultancy ready to try: Admin, an analyst and a complete sample customer.</summary>
public class DevelopmentSeederTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string Cnpj = "64899707000164";

    private static IConfiguration Settings(string admin, string analyst) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Seed:Admin:Username"] = admin,
        ["Seed:Admin:Password"] = Password,
        ["Seed:Admin:DisplayName"] = "Matheus Cardoso",
        ["Seed:Analyst:Username"] = analyst,
        ["Seed:Analyst:Password"] = Password,
        ["Seed:Analyst:DisplayName"] = "Ana Analista",
        ["Seed:Customer:Cnpj"] = "64.899.707/0001-64",
        ["Seed:Customer:Password"] = Password,
        ["Seed:Customer:DisplayName"] = "BZ Tech",
    }).Build();

    [Fact]
    public async Task Seeds_an_admin_an_analyst_and_a_complete_customer_once()
    {
        var admin = NewUsername("seed-admin");
        var analyst = NewUsername("seed-analyst");
        var client = factory.CreateClient(new() { HandleCookies = false });
        await client.GetAsync("/api/tenant"); // creates the test database

        await DevelopmentSeeder.SeedAsync(factory.Services, Settings(admin, analyst));
        await DevelopmentSeeder.SeedAsync(factory.Services, Settings(admin, analyst)); // a second start changes nothing

        var analystToken = await client.LoginInternalAsync(analyst);
        var me = await (await client.SendAsync(HttpMethod.Get, "/api/auth/me", analystToken)).JsonAsync();
        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Analyst");

        var customers = await (await client.SendAsync(HttpMethod.Get, "/api/customers?search=BZ", analystToken)).JsonAsync();
        var customer = customers.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        customer.GetProperty("cnpj").GetString().Should().Be(Cnpj);

        var login = await client.PostAsJsonAsync("/api/auth/customer/login", new { cnpj = Cnpj, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var customerToken = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        var company = await client.SendAsync(HttpMethod.Get, "/api/me/company", customerToken);
        company.StatusCode.Should().Be(HttpStatusCode.OK, "the sample customer is a complete company, not a bare login");
        (await company.JsonAsync()).GetProperty("contacts").GetArrayLength().Should().BeGreaterThan(0);
    }
}
