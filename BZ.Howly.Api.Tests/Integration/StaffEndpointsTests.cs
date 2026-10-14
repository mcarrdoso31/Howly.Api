using System.Net;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class StaffEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    [Fact]
    public async Task Analysts_list_the_active_staff_to_pick_an_assignee()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var username = NewUsername("staff");
        var analystId = await factory.CreateUserAsync(username, Password, $"Analyst {marker}", "Analyst");
        await factory.CreateUserAsync(NewUsername("adm"), Password, $"Admin {marker}", "Admin");
        await factory.CreateUserAsync("10000003000189", Password, $"Customer {marker}", "Customer");

        var response = await _client.SendAsync(HttpMethod.Get, "/api/staff", await _client.LoginInternalAsync(username));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var people = (await response.JsonAsync()).EnumerateArray().Where(p => p.GetProperty("name").GetString()!.Contains(marker)).ToList();
        people.Select(p => p.GetProperty("name").GetString()).Should().BeEquivalentTo([$"Analyst {marker}", $"Admin {marker}"]);
        people.Single(p => p.GetProperty("id").GetGuid() == analystId).TryGetProperty("avatarVersion", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Customers_cannot_list_the_staff()
    {
        await factory.CreateUserAsync("10000002000134", Password, "Customer Co", "Customer");
        var token = (await _client.LoginCustomerAsync("10000002000134", Password)).GetProperty("accessToken").GetString();

        (await _client.SendAsync(HttpMethod.Get, "/api/staff", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
