using System.Net;
using System.Text.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class ProcessTypeEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private async Task<string> StaffTokenAsync(string role = "Analyst")
    {
        var username = NewUsername("types");
        await factory.CreateUserAsync(username, Password, "Jane Analyst", role);
        return await _client.LoginInternalAsync(username);
    }

    private static object Step(string name, string owner = "Customer", string? agencyName = null, int? durationDays = 5, bool isOptional = false) =>
        new { name, description = (string?)null, owner, agencyName, durationDays, isOptional, customerChecklist = "Procuração", deliveryChecklist = (string?)null };

    private static object MtrBody(string name = "Emissão de MTR") => new
    {
        name,
        description = "Manifesto de transporte de resíduos",
        steps = new[]
        {
            Step("Coleta de documentos"),
            Step("Protocolo", "Staff"),
            Step("Análise do órgão", "Agency", "IBAMA", 30),
            Step("Pagamento da guia", "Payment", isOptional: true),
        },
    };

    private async Task<JsonElement> CreateAsync(string token, object? body = null)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/process-types", token, body ?? MtrBody());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.JsonAsync();
    }

    [Fact]
    public async Task Create_returns_the_type_with_its_ordered_steps()
    {
        var created = await CreateAsync(await StaffTokenAsync());

        created.GetProperty("name").GetString().Should().Be("Emissão de MTR");
        created.GetProperty("isActive").GetBoolean().Should().BeTrue();
        var steps = created.GetProperty("steps").EnumerateArray().ToList();
        steps.Select(s => s.GetProperty("name").GetString()).Should().Equal("Coleta de documentos", "Protocolo", "Análise do órgão", "Pagamento da guia");
        steps[2].GetProperty("owner").GetString().Should().Be("Agency");
        steps[2].GetProperty("agencyName").GetString().Should().Be("IBAMA");
        steps[3].GetProperty("isOptional").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Create_with_invalid_data_returns_400_pointing_to_the_step_field()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/process-types", await StaffTokenAsync(),
            new { name = "MTR", steps = new[] { Step("Coleta"), Step(" ") } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("steps[1].name", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_owner_returns_400()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/process-types", await StaffTokenAsync(),
            new { name = "MTR", steps = new[] { Step("Coleta", "Martians") } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_and_update_replace_the_steps()
    {
        var token = await StaffTokenAsync();
        var id = (await CreateAsync(token)).GetProperty("id").GetGuid();

        var updated = await _client.SendAsync(HttpMethod.Put, $"/api/process-types/{id}", token,
            new { name = "MTR", description = (string?)null, steps = new[] { Step("Protocolo", "Staff") } });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await (await _client.SendAsync(HttpMethod.Get, $"/api/process-types/{id}", token)).JsonAsync();
        body.GetProperty("name").GetString().Should().Be("MTR");
        body.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("name").GetString()).Should().Equal("Protocolo");
    }

    [Fact]
    public async Task List_shows_active_types_unless_inactive_ones_are_asked_for()
    {
        var token = await StaffTokenAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateAsync(token, MtrBody($"Active {marker}"));
        var inactive = (await CreateAsync(token, MtrBody($"Inactive {marker}"))).GetProperty("id").GetGuid();
        (await _client.SendAsync(HttpMethod.Post, $"/api/process-types/{inactive}/deactivate", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        async Task<string?[]> NamesAsync(string query) =>
            (await (await _client.SendAsync(HttpMethod.Get, $"/api/process-types{query}", token)).JsonAsync())
                .EnumerateArray().Select(t => t.GetProperty("name").GetString()).Where(n => n!.Contains(marker)).ToArray();

        (await NamesAsync("")).Should().Equal($"Active {marker}");
        (await NamesAsync("?includeInactive=true")).Should().BeEquivalentTo([$"Active {marker}", $"Inactive {marker}"]);
    }

    [Fact]
    public async Task List_items_carry_the_number_of_steps()
    {
        var token = await StaffTokenAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateAsync(token, MtrBody($"Count {marker}"));

        var item = (await (await _client.SendAsync(HttpMethod.Get, "/api/process-types", token)).JsonAsync())
            .EnumerateArray().Single(t => t.GetProperty("name").GetString() == $"Count {marker}");

        item.GetProperty("stepCount").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Reactivate_brings_the_type_back()
    {
        var token = await StaffTokenAsync();
        var id = (await CreateAsync(token)).GetProperty("id").GetGuid();
        await _client.SendAsync(HttpMethod.Post, $"/api/process-types/{id}/deactivate", token);

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/process-types/{id}/reactivate", token);

        (await response.JsonAsync()).GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_type_returns_404()
    {
        var response = await _client.SendAsync(HttpMethod.Get, $"/api/process-types/{Guid.NewGuid()}", await StaffTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customers_cannot_manage_types()
    {
        var cnpj = "10000004000123";
        await factory.CreateUserAsync(cnpj, Password, "Customer Co", "Customer");
        var token = (await _client.LoginCustomerAsync(cnpj, Password)).GetProperty("accessToken").GetString();

        (await _client.SendAsync(HttpMethod.Get, "/api/process-types", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
