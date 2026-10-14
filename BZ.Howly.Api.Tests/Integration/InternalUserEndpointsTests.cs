using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class InternalUserEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private async Task<string> TokenForAsync(string role)
    {
        var username = NewUsername(role.ToLowerInvariant());
        await factory.CreateUserAsync(username, Password, $"{role} User", role);
        return await _client.LoginInternalAsync(username);
    }

    [Theory]
    [InlineData("Analyst")]
    [InlineData("Admin")]
    public async Task Admin_creates_internal_user_with_temporary_password(string role)
    {
        var username = NewUsername("new");

        var response = await _client.SendAsync(HttpMethod.Post, "/api/internal-users", await TokenForAsync("Admin"),
            new { username = $"  {username.ToUpperInvariant()} ", displayName = "New Person", role });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.JsonAsync();
        body.GetProperty("username").GetString().Should().Be(username, "usernames are trimmed and stored in lowercase");
        body.GetProperty("role").GetString().Should().Be(role);
        var temporaryPassword = body.GetProperty("temporaryPassword").GetString()!;

        var login = await _client.PostAsJsonAsync("/api/auth/internal/login", new { username, password = temporaryPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        (await login.JsonAsync()).GetProperty("user").GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Analyst_cannot_create_internal_users()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/internal-users", await TokenForAsync("Analyst"),
            new { username = NewUsername("new"), displayName = "New Person", role = "Analyst" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Customer", "role")]
    [InlineData("Superuser", "role")]
    public async Task Create_with_role_other_than_analyst_or_admin_returns_400(string role, string field)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/internal-users", await TokenForAsync("Admin"),
            new { username = NewUsername("new"), displayName = "New Person", role });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("jane doe")]           // space
    [InlineData("jd")]                 // too short
    [InlineData("jane@doe")]           // symbol not allowed
    [InlineData("10000001000190")]     // a valid CNPJ would clash with customer logins
    public async Task Create_with_invalid_username_returns_400(string username)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/internal-users", await TokenForAsync("Admin"),
            new { username, displayName = "New Person", role = "Analyst" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("username", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_with_missing_display_name_returns_400()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/internal-users", await TokenForAsync("Admin"),
            new { username = NewUsername("new"), displayName = " ", role = "Analyst" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("displayName", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_with_existing_username_returns_409()
    {
        var admin = await TokenForAsync("Admin");
        var username = NewUsername("dup");
        await _client.SendAsync(HttpMethod.Post, "/api/internal-users", admin, new { username, displayName = "First", role = "Analyst" });

        var response = await _client.SendAsync(HttpMethod.Post, "/api/internal-users", admin, new { username, displayName = "Second", role = "Analyst" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Get_returns_internal_user_with_pending_flag_and_no_password()
    {
        var admin = await TokenForAsync("Admin");
        var created = await (await _client.SendAsync(HttpMethod.Post, "/api/internal-users", admin,
            new { username = NewUsername("new"), displayName = "New Person", role = "Analyst" })).JsonAsync();

        var body = await (await _client.SendAsync(HttpMethod.Get, $"/api/internal-users/{created.GetProperty("id").GetGuid()}", admin)).JsonAsync();

        body.GetProperty("displayName").GetString().Should().Be("New Person");
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Analyst");
        body.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
        body.TryGetProperty("temporaryPassword", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Get_and_reset_do_not_reach_customer_accounts()
    {
        var admin = await TokenForAsync("Admin");
        var customerId = await factory.CreateUserAsync("10000004000123", Password, "Customer Co", "Customer");

        var get = await _client.SendAsync(HttpMethod.Get, $"/api/internal-users/{customerId}", admin);
        var reset = await _client.SendAsync(HttpMethod.Post, $"/api/internal-users/{customerId}/reset-password", admin);

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        reset.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_resets_internal_user_password()
    {
        var admin = await TokenForAsync("Admin");
        var username = NewUsername("reset");
        var userId = await factory.CreateUserAsync(username, Password, "Forgetful Analyst", "Analyst");

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/internal-users/{userId}/reset-password", admin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var temporaryPassword = (await response.JsonAsync()).GetProperty("temporaryPassword").GetString();
        var login = await _client.PostAsJsonAsync("/api/auth/internal/login", new { username, password = temporaryPassword });
        (await login.JsonAsync()).GetProperty("user").GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    // ---- list ----

    [Fact]
    public async Task List_returns_staff_accounts_matching_the_search()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var analystUsername = NewUsername("list");
        await factory.CreateUserAsync(analystUsername, Password, $"Analyst {marker}", "Analyst");
        await factory.CreateUserAsync(NewUsername("adm"), Password, $"Admin {marker}", "Admin");

        var response = await _client.SendAsync(HttpMethod.Get, $"/api/internal-users?search={marker}", await TokenForAsync("Admin"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("totalCount").GetInt32().Should().Be(2);
        var analyst = body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("username").GetString() == analystUsername);
        analyst.GetProperty("displayName").GetString().Should().Be($"Analyst {marker}");
        analyst.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Analyst");
        analyst.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task List_finds_by_username_and_never_returns_customers()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        await factory.CreateUserAsync($"staff.{marker}", Password, "Someone", "Analyst");
        await factory.CreateUserAsync($"cust.{marker}", Password, "Customer Co", "Customer");

        var body = await (await _client.SendAsync(HttpMethod.Get, $"/api/internal-users?search={marker}", await TokenForAsync("Admin"))).JsonAsync();

        body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("username").GetString()).Should().Equal($"staff.{marker}");
    }

    [Fact]
    public async Task List_paginates()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
            await factory.CreateUserAsync(NewUsername("page"), Password, $"Page {marker} {i}", "Analyst");

        var body = await (await _client.SendAsync(HttpMethod.Get, $"/api/internal-users?search={marker}&page=2&pageSize=2", await TokenForAsync("Admin"))).JsonAsync();

        body.GetProperty("items").GetArrayLength().Should().Be(1);
        body.GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task List_as_analyst_returns_403()
    {
        var response = await _client.SendAsync(HttpMethod.Get, "/api/internal-users", await TokenForAsync("Analyst"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
