using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>The Admin edits, deactivates and reactivates staff accounts.</summary>
public class InternalUserManagementTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"6{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private async Task<(Guid Id, string Username, string Token)> UserAsync(string role, string name = "Staff Person")
    {
        var username = NewUsername(role.ToLowerInvariant());
        var id = await factory.CreateUserAsync(username, Password, name, role);
        return (id, username, await _client.LoginInternalAsync(username));
    }

    /// <summary>Opens a process assigned to <paramref name="assigneeId"/>.</summary>
    private async Task<Guid> ProcessAssignedToAsync(string staffToken, Guid assigneeId)
    {
        var contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var customer = await (await _client.SendAsync(HttpMethod.Post, "/api/customers", staffToken, new { cnpj = NewCnpj(), companyName = "Beagle Industrial Ltda", contacts })).JsonAsync();
        var type = await (await _client.SendAsync(HttpMethod.Post, "/api/process-types", staffToken, new
        {
            name = $"MTR {Guid.NewGuid():N}"[..12],
            steps = new object[] { new { name = "Coleta", owner = "Staff", isOptional = false } },
        })).JsonAsync();
        var process = await (await _client.SendAsync(HttpMethod.Post, "/api/processes", staffToken, new
        {
            customerId = customer.GetProperty("id").GetGuid(),
            processTypeId = type.GetProperty("id").GetGuid(),
            title = "MTR — Jundiaí",
            assigneeId,
        })).JsonAsync();
        return process.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> PostAsync(string url, string token, object? body = null) => _client.SendAsync(HttpMethod.Post, url, token, body ?? new { });

    [Fact]
    public async Task The_admin_renames_and_changes_the_role()
    {
        var (_, _, admin) = await UserAsync("Admin");
        var (id, _, _) = await UserAsync("Analyst");

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/internal-users/{id}", admin, new { displayName = "  Caio Souza  ", role = "Admin" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("displayName").GetString().Should().Be("Caio Souza");
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Admin");
    }

    [Theory]
    [InlineData("", "Analyst", "displayName")]
    [InlineData("Caio", "Customer", "role")]
    public async Task Editing_validates_name_and_role(string displayName, string role, string field)
    {
        var (_, _, admin) = await UserAsync("Admin");
        var (id, _, _) = await UserAsync("Analyst");

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/internal-users/{id}", admin, new { displayName, role });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_new_role_counts_at_once_not_only_when_the_token_expires()
    {
        var (_, _, admin) = await UserAsync("Admin");
        var (otherId, _, otherAdmin) = await UserAsync("Admin");

        await _client.SendAsync(HttpMethod.Put, $"/api/internal-users/{otherId}", admin, new { displayName = "Ex Admin", role = "Analyst" });

        (await _client.SendAsync(HttpMethod.Get, "/api/internal-users", otherAdmin)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivating_blocks_sign_in_and_ends_the_current_session()
    {
        var (_, _, admin) = await UserAsync("Admin");
        var (id, username, analyst) = await UserAsync("Analyst");

        var response = await PostAsync($"/api/internal-users/{id}/deactivate", admin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("isActive").GetBoolean().Should().BeFalse();
        (await _client.SendAsync(HttpMethod.Get, "/api/processes", analyst)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var login = await _client.PostAsJsonAsync("/api/auth/internal/login", new { username, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var list = await (await _client.SendAsync(HttpMethod.Get, $"/api/internal-users?search={username}", admin)).JsonAsync();
        list.GetProperty("items")[0].GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Reactivating_lets_the_person_sign_in_again()
    {
        var (_, _, admin) = await UserAsync("Admin");
        var (id, username, _) = await UserAsync("Analyst");
        await PostAsync($"/api/internal-users/{id}/deactivate", admin);

        var response = await PostAsync($"/api/internal-users/{id}/reactivate", admin);

        (await response.JsonAsync()).GetProperty("isActive").GetBoolean().Should().BeTrue();
        (await _client.PostAsJsonAsync("/api/auth/internal/login", new { username, password = Password })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nobody_deactivates_themselves()
    {
        var (id, _, admin) = await UserAsync("Admin");

        (await PostAsync($"/api/internal-users/{id}/deactivate", admin)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Open_processes_must_go_to_someone_else_before_deactivating()
    {
        var (_, _, admin) = await UserAsync("Admin");
        var (leavingId, _, _) = await UserAsync("Analyst", "Jane Analyst");
        var (heirId, _, heir) = await UserAsync("Analyst", "Caio Analyst");
        var processId = await ProcessAssignedToAsync(admin, leavingId);

        var detail = await (await _client.SendAsync(HttpMethod.Get, $"/api/internal-users/{leavingId}", admin)).JsonAsync();
        detail.GetProperty("openProcessCount").GetInt32().Should().Be(1);

        var withoutHeir = await PostAsync($"/api/internal-users/{leavingId}/deactivate", admin);
        withoutHeir.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await withoutHeir.JsonAsync()).GetProperty("errors").TryGetProperty("reassignTo", out _).Should().BeTrue();

        var response = await PostAsync($"/api/internal-users/{leavingId}/deactivate", admin, new { reassignTo = heirId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var process = await (await _client.SendAsync(HttpMethod.Get, $"/api/processes/{processId}", admin)).JsonAsync();
        process.GetProperty("assignee").GetProperty("id").GetGuid().Should().Be(heirId);
        var notifications = await (await _client.SendAsync(HttpMethod.Get, "/api/me/notifications", heir)).JsonAsync();
        notifications.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()).Should().Contain("Assigned");
    }

    [Fact]
    public async Task The_heir_must_be_another_active_staff_member()
    {
        var (_, _, admin) = await UserAsync("Admin");
        var (leavingId, _, _) = await UserAsync("Analyst");
        var (inactiveId, _, _) = await UserAsync("Analyst");
        await PostAsync($"/api/internal-users/{inactiveId}/deactivate", admin);
        await ProcessAssignedToAsync(admin, leavingId);

        foreach (var heir in new[] { leavingId, inactiveId, Guid.NewGuid() })
            (await PostAsync($"/api/internal-users/{leavingId}/deactivate", admin, new { reassignTo = heir }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

/// <summary>A fresh database with a single Admin: the system must never be left without one.</summary>
public class LastAdminTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    [Fact]
    public async Task The_last_active_admin_cannot_stop_being_admin()
    {
        var username = NewUsername("solo");
        var id = await factory.CreateUserAsync(username, Password, "Solo Admin", "Admin");
        var token = await _client.LoginInternalAsync(username);

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/internal-users/{id}", token, new { displayName = "Solo Admin", role = "Analyst" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.SendAsync(HttpMethod.Get, $"/api/internal-users/{id}", token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
