using System.Net;
using System.Text.Json;
using BZ.Howly.Api.Core.Identity;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>
/// The timeline of long-lived processes, in pages: the details bring the newest events (and whether there are older
/// ones); /events brings the older ones, newest first, page by page until the cursor ends. The customer sees only
/// what the details already show them, and only of their own processes.
/// </summary>
public class ProcessEventPagesEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const int DetailsEvents = 20;

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"8{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Scenario(HttpClient Client, string Staff, Guid TypeId);

    private record Customer(Guid Id, string Cnpj, string TemporaryPassword);

    private async Task<Scenario> ScenarioAsync()
    {
        var slug = $"evt-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Linha do tempo");
        var client = factory.CreatePortalClient(slug);
        var analyst = NewUsername("analyst");
        await factory.CreateUserInAsync(slug, analyst, Password, "Jane Analyst", Roles.Analyst);
        var staff = await client.LoginInternalAsync(analyst);
        var type = await (await client.SendAsync(HttpMethod.Post, "/api/process-types", staff,
            new { name = "LO", steps = new object[] { new { name = "Análise", owner = "Staff" } } })).JsonAsync();
        return new Scenario(client, staff, type.GetProperty("id").GetGuid());
    }

    private static readonly object[] Contacts = [new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true }];

    private static async Task<Customer> CustomerAsync(Scenario s)
    {
        var cnpj = NewCnpj();
        var customer = await (await s.Client.SendAsync(HttpMethod.Post, "/api/customers", s.Staff, new { document = cnpj, companyName = "Beagle", contacts = Contacts })).JsonAsync();
        return new Customer(customer.GetProperty("id").GetGuid(), cnpj, customer.GetProperty("temporaryPassword").GetString()!);
    }

    private static async Task<string> CustomerTokenAsync(Scenario s, Customer c)
    {
        const string newPassword = "Brand-New-Pass-2";
        var pending = (await s.Client.LoginCustomerAsync(c.Cnpj, c.TemporaryPassword)).GetProperty("accessToken").GetString();
        var changed = await (await s.Client.SendAsync(HttpMethod.Post, "/api/auth/change-password", pending,
            new { currentPassword = c.TemporaryPassword, newPassword })).JsonAsync();
        await s.Client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", changed.GetProperty("accessToken").GetString(), new { contacts = Contacts });
        return (await s.Client.LoginCustomerAsync(c.Cnpj, newPassword)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>A process with the "Opened" event plus <paramref name="comments"/> numbered comments ("c1", "c2"…).</summary>
    private static async Task<Guid> ProcessWithCommentsAsync(Scenario s, Customer c, int comments)
    {
        var process = await (await s.Client.SendAsync(HttpMethod.Post, "/api/processes", s.Staff,
            new { customerId = c.Id, processTypeId = s.TypeId, title = "LO — Jundiaí" })).JsonAsync();
        var id = process.GetProperty("id").GetGuid();
        for (var i = 1; i <= comments; i++)
            (await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{id}/comments", s.Staff, new { comment = $"c{i}" })).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<JsonElement> GetJsonAsync(Scenario s, string url, string? token = null)
    {
        var response = await s.Client.SendAsync(HttpMethod.Get, url, token ?? s.Staff);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    private static string?[] Comments(JsonElement events) =>
        events.EnumerateArray().Select(e => e.TryGetProperty("comment", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : e.GetProperty("type").GetString()).ToArray();

    /// <summary>Follows the cursor from the first page to the end.</summary>
    private static async Task<List<JsonElement>> AllPagesAsync(Scenario s, string baseUrl, int take, string? token = null)
    {
        var all = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var url = cursor is null ? $"{baseUrl}?take={take}" : $"{baseUrl}?take={take}&before={Uri.EscapeDataString(cursor)}";
            var page = await GetJsonAsync(s, url, token);
            page.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(take);
            all.AddRange(page.GetProperty("items").EnumerateArray());
            var next = page.GetProperty("nextCursor");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();
        } while (cursor is not null);
        return all;
    }

    [Fact]
    public async Task The_details_bring_only_the_newest_events_and_say_there_are_older_ones()
    {
        var s = await ScenarioAsync();
        var id = await ProcessWithCommentsAsync(s, await CustomerAsync(s), 25);

        var details = await GetJsonAsync(s, $"/api/processes/{id}");

        details.GetProperty("hasMoreEvents").GetBoolean().Should().BeTrue();
        // Same order as always (oldest of the page first), the newest at the end.
        Comments(details.GetProperty("events")).Should().Equal(Enumerable.Range(6, DetailsEvents).Select(i => $"c{i}"));
    }

    [Fact]
    public async Task A_short_timeline_comes_whole_in_the_details()
    {
        var s = await ScenarioAsync();
        var id = await ProcessWithCommentsAsync(s, await CustomerAsync(s), 2);

        var details = await GetJsonAsync(s, $"/api/processes/{id}");

        details.GetProperty("hasMoreEvents").GetBoolean().Should().BeFalse();
        Comments(details.GetProperty("events")).Should().Equal("Opened", "c1", "c2");
    }

    [Fact]
    public async Task The_pages_come_newest_first_and_end_without_repeating_or_skipping()
    {
        var s = await ScenarioAsync();
        var id = await ProcessWithCommentsAsync(s, await CustomerAsync(s), 25);

        var all = await AllPagesAsync(s, $"/api/processes/{id}/events", take: 7);

        Comments(System.Text.Json.JsonSerializer.SerializeToElement(all))
            .Should().Equal(Enumerable.Range(1, 25).Reverse().Select(i => $"c{i}").Append("Opened"));
        all.Select(e => e.GetProperty("id").GetGuid()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task The_first_page_has_twenty_events_by_default_and_never_more_than_a_hundred()
    {
        var s = await ScenarioAsync();
        var id = await ProcessWithCommentsAsync(s, await CustomerAsync(s), 25);

        var byDefault = await GetJsonAsync(s, $"/api/processes/{id}/events");
        var tooMany = await GetJsonAsync(s, $"/api/processes/{id}/events?take=1000");

        byDefault.GetProperty("items").GetArrayLength().Should().Be(20);
        byDefault.GetProperty("nextCursor").GetString().Should().NotBeNullOrEmpty();
        tooMany.GetProperty("items").GetArrayLength().Should().Be(26);
        tooMany.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_cursor_of_the_details_brings_what_comes_before_them()
    {
        var s = await ScenarioAsync();
        var id = await ProcessWithCommentsAsync(s, await CustomerAsync(s), 25);
        var details = await GetJsonAsync(s, $"/api/processes/{id}");

        var older = await GetJsonAsync(s, $"/api/processes/{id}/events?before={Uri.EscapeDataString(details.GetProperty("eventsCursor").GetString()!)}");

        Comments(older.GetProperty("items")).Should().Equal("c5", "c4", "c3", "c2", "c1", "Opened");
        older.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_cursor_that_is_not_one_is_refused()
    {
        var s = await ScenarioAsync();
        var id = await ProcessWithCommentsAsync(s, await CustomerAsync(s), 1);

        var response = await s.Client.SendAsync(HttpMethod.Get, $"/api/processes/{id}/events?before=abc", s.Staff);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("before", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_processes_and_processes_of_other_consultancies_are_not_found()
    {
        var s = await ScenarioAsync();
        var elsewhere = await ScenarioAsync();
        var foreign = await ProcessWithCommentsAsync(elsewhere, await CustomerAsync(elsewhere), 1);

        (await s.Client.SendAsync(HttpMethod.Get, $"/api/processes/{Guid.NewGuid()}/events", s.Staff)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Client.SendAsync(HttpMethod.Get, $"/api/processes/{foreign}/events", s.Staff)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_customer_pages_through_their_own_timeline_without_the_internal_notes()
    {
        var s = await ScenarioAsync();
        var customer = await CustomerAsync(s);
        var id = await ProcessWithCommentsAsync(s, customer, 21);
        for (var i = 1; i <= 3; i++)
            (await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{id}/notes", s.Staff, new { comment = $"note{i}" })).EnsureSuccessStatusCode();
        var token = await CustomerTokenAsync(s, customer);

        var details = await GetJsonAsync(s, $"/api/me/processes/{id}", token);
        var all = await AllPagesAsync(s, $"/api/me/processes/{id}/events", take: 5, token);

        details.GetProperty("hasMoreEvents").GetBoolean().Should().BeTrue();
        Comments(details.GetProperty("events")).Should().Equal(Enumerable.Range(2, DetailsEvents).Select(i => $"c{i}"));
        Comments(System.Text.Json.JsonSerializer.SerializeToElement(all))
            .Should().Equal(Enumerable.Range(1, 21).Reverse().Select(i => $"c{i}").Append("Opened"));
    }

    [Fact]
    public async Task The_staff_pages_include_the_internal_notes()
    {
        var s = await ScenarioAsync();
        var id = await ProcessWithCommentsAsync(s, await CustomerAsync(s), 1);
        (await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{id}/notes", s.Staff, new { comment = "note" })).EnsureSuccessStatusCode();

        var page = await GetJsonAsync(s, $"/api/processes/{id}/events");

        Comments(page.GetProperty("items")).Should().Equal("note", "c1", "Opened");
    }

    [Fact]
    public async Task A_customer_cannot_page_through_someone_elses_process()
    {
        var s = await ScenarioAsync();
        var mine = await CustomerAsync(s);
        var other = await CustomerAsync(s);
        var theirs = await ProcessWithCommentsAsync(s, other, 1);
        var token = await CustomerTokenAsync(s, mine);

        var response = await s.Client.SendAsync(HttpMethod.Get, $"/api/me/processes/{theirs}/events", token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
