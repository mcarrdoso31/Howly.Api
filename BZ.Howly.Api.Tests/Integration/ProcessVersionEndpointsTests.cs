using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BZ.Howly.Api.Core.Identity;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>
/// A screen opened minutes ago acting on what it shows: the process carries its version (and an ETag), and the actions
/// that move it may send it back in If-Match. Someone changed it since: 409 "stale_version", nothing done. Without
/// If-Match everything works as before; comments and documents never ask for it.
/// </summary>
public class ProcessVersionEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"7{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Scenario(HttpClient Client, string Staff, Guid StaffId, Guid CustomerId, string CustomerCnpj, string CustomerPassword, Guid TypeId);

    private async Task<Scenario> ScenarioAsync()
    {
        var slug = $"ver-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Versões");
        var client = factory.CreatePortalClient(slug);
        var analyst = NewUsername("analyst");
        var staffId = await factory.CreateUserInAsync(slug, analyst, Password, "Jane Analyst", Roles.Analyst);
        var staff = await client.LoginInternalAsync(analyst);

        var cnpj = NewCnpj();
        var contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var customer = await (await client.SendAsync(HttpMethod.Post, "/api/customers", staff, new { document = cnpj, companyName = "Beagle", contacts })).JsonAsync();
        var type = await (await client.SendAsync(HttpMethod.Post, "/api/process-types", staff,
            new { name = "LO", steps = new object[] { new { name = "Análise", owner = "Staff" }, new { name = "Protocolo", owner = "Staff" } } })).JsonAsync();
        return new Scenario(client, staff, staffId, customer.GetProperty("id").GetGuid(), cnpj,
            customer.GetProperty("temporaryPassword").GetString()!, type.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> OpenAsync(Scenario s, string title = "LO — Jundiaí") =>
        await (await s.Client.SendAsync(HttpMethod.Post, "/api/processes", s.Staff, new { customerId = s.CustomerId, processTypeId = s.TypeId, title })).JsonAsync();

    private static async Task<JsonElement> GetAsync(Scenario s, Guid id) =>
        await (await s.Client.SendAsync(HttpMethod.Get, $"/api/processes/{id}", s.Staff)).JsonAsync();

    private static Task<HttpResponseMessage> SendIfMatchAsync(Scenario s, HttpMethod method, string url, string? ifMatch, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Staff);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null)
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        return s.Client.SendAsync(request);
    }

    private static string Quoted(string version) => $"\"{version}\"";

    private static async Task<Guid> AgencyAsync(Scenario s) =>
        (await (await s.Client.SendAsync(HttpMethod.Post, "/api/agencies", s.Staff,
            new { acronym = "CETESB", name = "Companhia Ambiental", sphere = "State", state = "SP" })).JsonAsync()).GetProperty("id").GetGuid();

    private async Task<string> CustomerTokenAsync(Scenario s)
    {
        const string newPassword = "Brand-New-Pass-2";
        var contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var pending = (await s.Client.LoginCustomerAsync(s.CustomerCnpj, s.CustomerPassword)).GetProperty("accessToken").GetString();
        var changed = await (await s.Client.SendAsync(HttpMethod.Post, "/api/auth/change-password", pending,
            new { currentPassword = s.CustomerPassword, newPassword })).JsonAsync();
        await s.Client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", changed.GetProperty("accessToken").GetString(), new { contacts });
        return (await s.Client.LoginCustomerAsync(s.CustomerCnpj, newPassword)).GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task The_details_carry_the_version_and_an_etag_that_changes_with_the_process()
    {
        var s = await ScenarioAsync();
        var opened = await OpenAsync(s);
        var id = opened.GetProperty("id").GetGuid();

        var response = await s.Client.SendAsync(HttpMethod.Get, $"/api/processes/{id}", s.Staff);
        var version = (await response.JsonAsync()).GetProperty("version").GetString();

        version.Should().NotBeNullOrWhiteSpace();
        response.Headers.ETag!.Tag.Should().Be(Quoted(version!));

        var advanced = await (await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{id}/advance", s.Staff, new { })).JsonAsync();
        advanced.GetProperty("version").GetString().Should().NotBe(version);
    }

    [Fact]
    public async Task The_customer_view_carries_the_version_too()
    {
        var s = await ScenarioAsync();
        var id = (await OpenAsync(s)).GetProperty("id").GetGuid();
        var customer = await CustomerTokenAsync(s);
        var staffVersion = (await GetAsync(s, id)).GetProperty("version").GetString();

        var response = await s.Client.SendAsync(HttpMethod.Get, $"/api/me/processes/{id}", customer);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("version").GetString().Should().Be(staffVersion);
        response.Headers.ETag!.Tag.Should().Be(Quoted(staffVersion!));
    }

    [Fact]
    public async Task Acting_on_the_version_on_screen_works()
    {
        var s = await ScenarioAsync();
        var id = (await OpenAsync(s)).GetProperty("id").GetGuid();
        var version = (await GetAsync(s, id)).GetProperty("version").GetString()!;

        var response = await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/advance", Quoted(version), new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("steps")[1].GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task Acting_on_an_older_version_is_refused_as_stale_and_changes_nothing()
    {
        var s = await ScenarioAsync();
        var id = (await OpenAsync(s)).GetProperty("id").GetGuid();
        var onScreen = (await GetAsync(s, id)).GetProperty("version").GetString()!;
        // Meanwhile someone else changed the process.
        (await s.Client.SendAsync(HttpMethod.Put, $"/api/processes/{id}/title", s.Staff, new { title = "LO — Jundiaí (renomeado)" })).EnsureSuccessStatusCode();

        var response = await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/advance", Quoted(onScreen), new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.JsonAsync()).GetProperty("code").GetString().Should().Be("stale_version");
        var stored = await GetAsync(s, id);
        stored.GetProperty("steps")[0].GetProperty("status").GetString().Should().Be("Active");
        stored.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()).Should().NotContain("StepAdvanced");
    }

    [Fact]
    public async Task Without_if_match_the_actions_work_as_before()
    {
        var s = await ScenarioAsync();
        var id = (await OpenAsync(s)).GetProperty("id").GetGuid();
        (await s.Client.SendAsync(HttpMethod.Put, $"/api/processes/{id}/title", s.Staff, new { title = "Outro título" })).EnsureSuccessStatusCode();

        var response = await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/advance", null, new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Any_version_matches_the_star()
    {
        var s = await ScenarioAsync();
        var id = (await OpenAsync(s)).GetProperty("id").GetGuid();

        var response = await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/advance", "*", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Comments_and_notes_on_an_older_screen_still_go_through()
    {
        var s = await ScenarioAsync();
        var id = (await OpenAsync(s)).GetProperty("id").GetGuid();
        var onScreen = (await GetAsync(s, id)).GetProperty("version").GetString()!;
        (await s.Client.SendAsync(HttpMethod.Put, $"/api/processes/{id}/title", s.Staff, new { title = "Outro título" })).EnsureSuccessStatusCode();

        var comment = await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/comments", Quoted(onScreen), new { comment = "Enviei o protocolo." });
        var note = await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/notes", Quoted(onScreen), new { comment = "Ligar amanhã." });

        comment.StatusCode.Should().Be(HttpStatusCode.OK);
        note.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public static TheoryData<string> Actions =>
    [
        "skip", "return", "pause", "resume", "cancel", "assign", "license", "renewal", "title", "agency",
    ];

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Every_action_that_moves_the_process_checks_the_version(string action)
    {
        var s = await ScenarioAsync();
        var id = (await OpenAsync(s)).GetProperty("id").GetGuid();
        var stale = Quoted(Guid.NewGuid().ToString("N"));
        var stepId = (await GetAsync(s, id)).GetProperty("steps")[0].GetProperty("id").GetGuid();

        var response = action switch
        {
            "skip" => await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/skip", stale, new { }),
            "return" => await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/return", stale, new { stepId, comment = "Correções." }),
            "pause" => await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/pause", stale, new { reason = "Aguardando." }),
            "resume" => await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/resume", stale, new { }),
            "cancel" => await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/cancel", stale, new { reason = "Desistiu." }),
            "assign" => await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/assign", stale, new { assigneeId = s.StaffId }),
            "license" => await SendIfMatchAsync(s, HttpMethod.Put, $"/api/processes/{id}/license", stale, new { expiresOn = "2031-10-15", renewalStartsOn = (string?)null }),
            "renewal" => await SendIfMatchAsync(s, HttpMethod.Post, $"/api/processes/{id}/renewal", stale, new { title = "Renovação" }),
            "title" => await SendIfMatchAsync(s, HttpMethod.Put, $"/api/processes/{id}/title", stale, new { title = "Outro" }),
            "agency" => await SendIfMatchAsync(s, HttpMethod.Put, $"/api/processes/{id}/steps/{stepId}/agency", stale, new { agencyId = await AgencyAsync(s) }),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.JsonAsync()).GetProperty("code").GetString().Should().Be("stale_version");
    }
}
