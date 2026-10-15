using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Integration;

public class AuthEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string Password = "Correct-Horse-1";
    private const string RefreshCookie = "howly_refresh";

    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    // Each test gets its own user so lockout and token state never leak between tests.
    private static string NewUsername() => $"analyst.{Guid.NewGuid():N}"[..20];

    // ---- customer login ----

    [Fact]
    public async Task CustomerLogin_returns_access_token_and_user()
    {
        await factory.CreateUserAsync("11222333000181", Password, "ACME Industrial", "Customer");

        var response = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = "11.222.333/0001-81", password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("expiresAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
        body.GetProperty("user").GetProperty("name").GetString().Should().Be("ACME Industrial");
        body.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Customer");
        body.TryGetProperty("refreshToken", out _).Should().BeFalse("the refresh token must only travel in the cookie");
    }

    [Fact]
    public async Task CustomerLogin_sets_refresh_cookie_as_httponly_secure_and_scoped_to_auth_routes()
    {
        await factory.CreateUserAsync("12ABC34501DE35", Password, "Beagle Co", "Customer");

        var response = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = "12ABC34501DE35", password = Password });

        var cookie = GetSetCookie(response).ToLowerInvariant();
        cookie.Should().StartWith($"{RefreshCookie}=");
        cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("path=/api/auth").And.Contain("samesite=strict");
    }

    [Fact]
    public async Task CustomerLogin_with_wrong_password_returns_401_problem_without_cookie()
    {
        await factory.CreateUserAsync("10000001000190", Password, "Wrong Password Co", "Customer");

        var response = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = "10000001000190", password = "wrong-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task CustomerLogin_locks_the_account_after_five_failed_attempts()
    {
        await factory.CreateUserAsync("10000002000134", Password, "Locked Co", "Customer");

        for (var i = 0; i < 5; i++)
            await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = "10000002000134", password = "wrong-password" });

        var response = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = "10000002000134", password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CustomerLogin_succeeds_after_fewer_than_five_failed_attempts()
    {
        await factory.CreateUserAsync("10000003000189", Password, "Almost Locked Co", "Customer");

        for (var i = 0; i < 4; i++)
            await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = "10000003000189", password = "wrong-password" });

        var response = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = "10000003000189", password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- internal login ----

    [Fact]
    public async Task InternalLogin_returns_access_token_for_analyst()
    {
        var username = NewUsername();
        await factory.CreateUserAsync(username, Password, "Jane Doe", "Analyst");

        var response = await _client.PostAsJsonAsync("/api/auth/internal/login", new { username, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Analyst");
    }

    [Fact]
    public async Task InternalLogin_with_customer_credentials_returns_401()
    {
        await factory.CreateUserAsync("10000004000123", Password, "Sneaky Customer", "Customer");

        var response = await _client.PostAsJsonAsync("/api/auth/internal/login", new { username = "10000004000123", password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- me ----

    [Fact]
    public async Task Me_with_bearer_token_returns_current_user()
    {
        var username = NewUsername();
        var userId = await factory.CreateUserAsync(username, Password, "Jane Doe", "Analyst", "Admin");
        var login = await LoginInternalAsync(username);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(userId);
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().BeEquivalentTo("Analyst", "Admin");
    }

    [Fact]
    public async Task Me_without_token_returns_401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_with_tampered_token_returns_401()
    {
        var username = NewUsername();
        await factory.CreateUserAsync(username, Password, "Jane Doe", "Analyst");
        var login = await LoginInternalAsync(username);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken[..^4] + "AAAA");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- refresh ----

    [Fact]
    public async Task Refresh_with_cookie_returns_new_access_token_and_rotates_cookie()
    {
        var username = NewUsername();
        await factory.CreateUserAsync(username, Password, "Jane Doe", "Analyst");
        var login = await LoginInternalAsync(username);

        var response = await PostWithRefreshCookieAsync("/api/auth/refresh", login.RefreshCookieValue);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        CookieValue(GetSetCookie(response)).Should().NotBe(login.RefreshCookieValue);
    }

    [Fact]
    public async Task Refresh_reusing_old_cookie_returns_401()
    {
        var username = NewUsername();
        await factory.CreateUserAsync(username, Password, "Jane Doe", "Analyst");
        var login = await LoginInternalAsync(username);
        await PostWithRefreshCookieAsync("/api/auth/refresh", login.RefreshCookieValue);

        var response = await PostWithRefreshCookieAsync("/api/auth/refresh", login.RefreshCookieValue);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_without_cookie_returns_401()
    {
        var response = await _client.PostAsync("/api/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- logout ----

    [Fact]
    public async Task Logout_revokes_refresh_token_and_expires_cookie()
    {
        var username = NewUsername();
        await factory.CreateUserAsync(username, Password, "Jane Doe", "Analyst");
        var login = await LoginInternalAsync(username);

        var logout = await PostWithRefreshCookieAsync("/api/auth/logout", login.RefreshCookieValue);
        var refreshAfterLogout = await PostWithRefreshCookieAsync("/api/auth/refresh", login.RefreshCookieValue);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        GetSetCookie(logout).ToLowerInvariant().Should().Contain("expires=thu, 01 jan 1970");
        refreshAfterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- helpers ----

    private async Task<(string AccessToken, string RefreshCookieValue)> LoginInternalAsync(string username)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/internal/login", new { username, password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("accessToken").GetString()!, CookieValue(GetSetCookie(response)));
    }

    private Task<HttpResponseMessage> PostWithRefreshCookieAsync(string url, string cookieValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"{RefreshCookie}={cookieValue}");
        return _client.SendAsync(request);
    }

    private static string GetSetCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith($"{RefreshCookie}="));

    private static string CookieValue(string setCookie) => setCookie.Split(';')[0][(RefreshCookie.Length + 1)..];
}

public class LoginRateLimitTests(LoginRateLimitTests.StrictRateLimitFactory factory)
    : IClassFixture<LoginRateLimitTests.StrictRateLimitFactory>
{
    public class StrictRateLimitFactory : HowlyApiFactory
    {
        protected override int LoginPermitLimit => 3;
    }

    [Fact]
    public async Task Login_endpoints_reject_requests_over_the_limit_with_429()
    {
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/internal/login", new { username = "nobody", password = "wrong-password" });
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Equal(
            HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests);
    }
}
