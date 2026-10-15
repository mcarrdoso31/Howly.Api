using System.Net;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>Customers that are people (CPF), not only companies (CNPJ): small clients are part of the product.</summary>
public class PersonCustomerEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    // Fictitious CPFs: a unique base per call plus the two check digits.
    private static int _sequence;
    private static string NewCpf()
    {
        var body = $"3{Interlocked.Increment(ref _sequence):D8}";
        int Digit(string s, int start) { var r = s.Select((c, i) => (c - '0') * (start - i)).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, 10);
        return $"{body}{first}{Digit(body + first, 11)}";
    }

    private async Task<string> AnalystTokenAsync()
    {
        var username = NewUsername("analyst");
        await factory.CreateUserAsync(username, Password, "Jane Analyst", "Analyst");
        return await _client.LoginInternalAsync(username);
    }

    private static object Body(string document) => new
    {
        document,
        companyName = "João da Silva",
        contacts = new[] { new { name = "João", email = "joao@sitio.example", phone = "(11) 91234-5678", receivesNotices = true } },
    };

    [Fact]
    public async Task A_person_is_a_customer_by_cpf_and_signs_in_with_it()
    {
        var token = await AnalystTokenAsync();
        var cpf = NewCpf();
        var masked = $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";

        var created = await _client.SendAsync(HttpMethod.Post, "/api/customers", token, Body(masked));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await created.JsonAsync();
        customer.GetProperty("document").GetString().Should().Be(cpf);
        var temporary = customer.GetProperty("temporaryPassword").GetString()!;

        var login = await _client.SendAsync(HttpMethod.Post, "/api/auth/customer/login", null, new { document = masked, password = temporary });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await (await _client.SendAsync(HttpMethod.Get, $"/api/customers?search={cpf[..6]}", token)).JsonAsync();
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("document").GetString()).Should().Contain(cpf);
    }

    [Fact]
    public async Task The_same_cpf_cannot_be_registered_twice()
    {
        var token = await AnalystTokenAsync();
        var cpf = NewCpf();
        await _client.SendAsync(HttpMethod.Post, "/api/customers", token, Body(cpf));

        var again = await _client.SendAsync(HttpMethod.Post, "/api/customers", token, Body(cpf));

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_invalid_document_points_to_the_document_field()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/customers", await AnalystTokenAsync(), Body("529.982.247-24"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("document", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Staff_usernames_cannot_look_like_a_cpf()
    {
        var username = NewUsername("admin");
        await factory.CreateUserAsync(username, Password, "Ada Admin", "Admin");
        var admin = await _client.LoginInternalAsync(username);

        var response = await _client.SendAsync(HttpMethod.Post, "/api/internal-users", admin, new { username = NewCpf(), displayName = "Alguém", role = "Analyst" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("username", out _).Should().BeTrue();
    }
}
