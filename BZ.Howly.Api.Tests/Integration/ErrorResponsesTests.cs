using System.Net;
using BZ.Howly.Api.DAO.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>Every error the API answers has the same JSON shape (problem details), never an HTML page.</summary>
public class ErrorResponsesTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private async Task<string> StaffTokenAsync(string? username = null)
    {
        username ??= NewUsername("errors");
        await factory.CreateUserAsync(username, Password, "Jane Analyst", "Analyst");
        return await _client.LoginInternalAsync(username);
    }

    [Fact]
    public async Task Unknown_routes_answer_problem_details()
    {
        var response = await _client.SendAsync(HttpMethod.Get, "/api/does-not-exist", await StaffTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.JsonAsync()).GetProperty("status").GetInt32().Should().Be(404);
    }

    [Fact]
    public async Task Wrong_methods_answer_problem_details()
    {
        var response = await _client.SendAsync(HttpMethod.Delete, "/api/process-types", await StaffTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Missing_sessions_answer_problem_details()
    {
        var response = await _client.SendAsync(HttpMethod.Get, "/api/processes", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task A_user_removed_during_the_session_gets_401_instead_of_a_server_error()
    {
        // A real process, opened by an analyst who stays.
        var token = await StaffTokenAsync();
        var contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var customer = await (await _client.SendAsync(HttpMethod.Post, "/api/customers", token,
            new { document = "10000004000123", companyName = "Beagle Industrial Ltda", contacts })).JsonAsync();
        var type = await (await _client.SendAsync(HttpMethod.Post, "/api/process-types", token,
            new { name = "Tipo", steps = new[] { new { name = "Etapa", owner = "Staff", isOptional = false } } })).JsonAsync();
        var process = await (await _client.SendAsync(HttpMethod.Post, "/api/processes", token,
            new { customerId = customer.GetProperty("id").GetGuid(), processTypeId = type.GetProperty("id").GetGuid(), title = "MTR" })).JsonAsync();

        // Another analyst signs in, then the account is removed while the access token is still valid.
        var username = NewUsername("gone");
        var goneToken = await StaffTokenAsync(username);
        using (var scope = factory.CreateTenantScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            (await users.DeleteAsync((await users.FindByNameAsync(username))!)).Succeeded.Should().BeTrue();
        }

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/processes/{process.GetProperty("id").GetGuid()}/comments", goneToken, new { comment = "oi" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}
