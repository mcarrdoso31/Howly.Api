using System.Net;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class PreferencesEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static readonly object DarkForest = new { themeMode = "dark", themePreset = "forest", primaryColor = (string?)null, language = "en" };

    private async Task<string> TokenAsync()
    {
        var username = NewUsername("prefs");
        await factory.CreateUserAsync(username, Password, "Prefs User", "Analyst");
        return await _client.LoginInternalAsync(username);
    }

    [Fact]
    public async Task Get_returns_204_while_the_user_never_saved_preferences()
    {
        var response = await _client.SendAsync(HttpMethod.Get, "/api/me/preferences", await TokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Put_saves_and_get_returns_the_saved_preferences()
    {
        var token = await TokenAsync();

        var put = await _client.SendAsync(HttpMethod.Put, "/api/me/preferences", token,
            new { themeMode = "dark", themePreset = "ocean", primaryColor = "#E11D48", language = "es" });
        var get = await _client.SendAsync(HttpMethod.Get, "/api/me/preferences", token);

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.JsonAsync();
        body.GetProperty("themeMode").GetString().Should().Be("dark");
        body.GetProperty("themePreset").GetString().Should().Be("ocean");
        body.GetProperty("primaryColor").GetString().Should().Be("#e11d48");
        body.GetProperty("language").GetString().Should().Be("es");
    }

    [Fact]
    public async Task Put_twice_keeps_only_the_latest_values()
    {
        var token = await TokenAsync();
        await _client.SendAsync(HttpMethod.Put, "/api/me/preferences", token, DarkForest);

        await _client.SendAsync(HttpMethod.Put, "/api/me/preferences", token,
            new { themeMode = "light", themePreset = "graphite", primaryColor = (string?)null, language = "pt-BR" });

        var body = await (await _client.SendAsync(HttpMethod.Get, "/api/me/preferences", token)).JsonAsync();
        body.GetProperty("themeMode").GetString().Should().Be("light");
        body.GetProperty("themePreset").GetString().Should().Be("graphite");
        body.GetProperty("language").GetString().Should().Be("pt-BR");
    }

    [Fact]
    public async Task Put_with_invalid_values_returns_400_per_field()
    {
        var response = await _client.SendAsync(HttpMethod.Put, "/api/me/preferences", await TokenAsync(),
            new { themeMode = "neon", themePreset = "forest", primaryColor = "blue", language = "fr" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await response.JsonAsync()).GetProperty("errors");
        errors.TryGetProperty("themeMode", out _).Should().BeTrue();
        errors.TryGetProperty("primaryColor", out _).Should().BeTrue();
        errors.TryGetProperty("language", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Preferences_belong_to_each_user()
    {
        var first = await TokenAsync();
        var second = await TokenAsync();
        await _client.SendAsync(HttpMethod.Put, "/api/me/preferences", first, DarkForest);

        var response = await _client.SendAsync(HttpMethod.Get, "/api/me/preferences", second);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Users_with_a_temporary_password_can_still_use_preferences()
    {
        var adminUsername = NewUsername("admin");
        await factory.CreateUserAsync(adminUsername, Password, "Admin", "Admin");
        var created = await (await _client.SendAsync(HttpMethod.Post, "/api/internal-users", await _client.LoginInternalAsync(adminUsername),
            new { username = NewUsername("pending"), displayName = "Pending", role = "Analyst" })).JsonAsync();
        var pendingToken = await _client.LoginInternalAsync(
            created.GetProperty("username").GetString()!, created.GetProperty("temporaryPassword").GetString()!);

        var put = await _client.SendAsync(HttpMethod.Put, "/api/me/preferences", pendingToken, DarkForest);

        put.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var response = await _client.SendAsync(HttpMethod.Get, "/api/me/preferences", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
