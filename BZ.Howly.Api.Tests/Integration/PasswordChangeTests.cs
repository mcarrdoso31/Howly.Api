using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class PasswordChangeTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";

    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    /// <summary>Creates an analyst through the admin endpoint, so it starts with a temporary password.</summary>
    private async Task<(string Username, string TemporaryPassword, string Token)> PendingAnalystAsync()
    {
        var adminUsername = NewUsername("admin");
        await factory.CreateUserAsync(adminUsername, Password, "Admin", "Admin");
        var adminToken = await _client.LoginInternalAsync(adminUsername);

        var username = NewUsername("pending");
        var created = await (await _client.SendAsync(HttpMethod.Post, "/api/internal-users", adminToken,
            new { username, displayName = "Pending Analyst", role = "Analyst" })).JsonAsync();
        var temporaryPassword = created.GetProperty("temporaryPassword").GetString()!;

        return (username, temporaryPassword, await _client.LoginInternalAsync(username, temporaryPassword));
    }

    // ---- blocking until the password changes ----

    [Fact]
    public async Task User_with_temporary_password_is_blocked_from_regular_endpoints()
    {
        var (_, _, token) = await PendingAnalystAsync();

        var response = await _client.SendAsync(HttpMethod.Get, "/api/customers", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.JsonAsync()).GetProperty("title").GetString().Should().Be("Password change required.");
    }

    [Fact]
    public async Task User_with_temporary_password_can_still_read_own_profile()
    {
        var (_, _, token) = await PendingAnalystAsync();

        var response = await _client.SendAsync(HttpMethod.Get, "/api/auth/me", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    // ---- changing the password ----

    [Fact]
    public async Task ChangePassword_returns_unblocked_session_and_rotates_cookie()
    {
        var (_, temporaryPassword, token) = await PendingAnalystAsync();

        var response = await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = temporaryPassword, newPassword = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("user").GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
        response.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("howly_refresh="));

        var newToken = body.GetProperty("accessToken").GetString();
        (await _client.SendAsync(HttpMethod.Get, "/api/customers", newToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task After_change_only_the_new_password_works()
    {
        var (username, temporaryPassword, token) = await PendingAnalystAsync();
        await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = temporaryPassword, newPassword = NewPassword });

        var withOld = await _client.PostAsJsonAsync("/api/auth/internal/login", new { username, password = temporaryPassword });
        var withNew = await _client.PostAsJsonAsync("/api/auth/internal/login", new { username, password = NewPassword });

        withOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        withNew.StatusCode.Should().Be(HttpStatusCode.OK);
        (await withNew.JsonAsync()).GetProperty("user").GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ChangePassword_with_wrong_current_password_returns_400_on_current_password()
    {
        var (_, _, token) = await PendingAnalystAsync();

        var response = await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = "Not-The-Password-9", newPassword = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("currentPassword", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_accepts_the_shortest_password_meeting_every_rule()
    {
        var (_, temporaryPassword, token) = await PendingAnalystAsync();

        var response = await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = temporaryPassword, newPassword = "Short1!A" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Ab1!")]                // too short
    [InlineData("alllowercase1!")]      // no uppercase
    [InlineData("NoDigitsHere!")]       // no digit
    [InlineData("NoSymbols123")]        // no symbol
    public async Task ChangePassword_with_weak_password_returns_400_on_new_password(string weak)
    {
        var (_, temporaryPassword, token) = await PendingAnalystAsync();

        var response = await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", token,
            new { currentPassword = temporaryPassword, newPassword = weak });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("newPassword", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_without_token_returns_401()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", null,
            new { currentPassword = "whatever-1A!", newPassword = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
