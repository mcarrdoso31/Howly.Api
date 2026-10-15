using System.Net;
using BZ.Howly.Api.Core.Identity;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>Up to three processes each staff member keeps at hand, saved on the account (computer and phone alike).</summary>
public class PinnedProcessesEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string Url = "/api/me/pinned-processes";

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"4{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Consultancy(HttpClient Client, string Slug, string Staff, Guid CustomerId, Guid TypeId);

    /// <summary>A consultancy of its own, with an analyst, a customer and a process type to open processes from.</summary>
    private async Task<Consultancy> ConsultancyAsync()
    {
        var slug = $"pins-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Fixados");
        var client = factory.CreatePortalClient(slug);
        var analyst = NewUsername("analyst");
        await factory.CreateUserInAsync(slug, analyst, Password, "Jane Analyst", Roles.Analyst);
        var staff = await client.LoginInternalAsync(analyst);

        var contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var customer = await (await client.SendAsync(HttpMethod.Post, "/api/customers", staff, new { document = NewCnpj(), companyName = "Beagle", contacts })).JsonAsync();
        var type = await (await client.SendAsync(HttpMethod.Post, "/api/process-types", staff,
            new { name = "LO", steps = new object[] { new { name = "Análise", owner = "Staff", durationDays = 10 } } })).JsonAsync();
        return new Consultancy(client, slug, staff, customer.GetProperty("id").GetGuid(), type.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> OpenProcessAsync(Consultancy c, string title)
    {
        var process = await (await c.Client.SendAsync(HttpMethod.Post, "/api/processes", c.Staff,
            new { customerId = c.CustomerId, processTypeId = c.TypeId, title })).JsonAsync();
        return process.GetProperty("id").GetGuid();
    }

    private async Task<string> AnotherAnalystAsync(Consultancy c)
    {
        var username = NewUsername("other");
        await factory.CreateUserInAsync(c.Slug, username, Password, "Bia Analyst", Roles.Analyst);
        return await c.Client.LoginInternalAsync(username);
    }

    private static async Task<Guid[]> PinnedAsync(HttpClient client, string token)
    {
        var body = await (await client.SendAsync(HttpMethod.Get, Url, token)).JsonAsync();
        return body.GetProperty("processIds").EnumerateArray().Select(e => e.GetGuid()).ToArray();
    }

    [Fact]
    public async Task Nobody_starts_with_pinned_processes()
    {
        var c = await ConsultancyAsync();

        var response = await c.Client.SendAsync(HttpMethod.Get, Url, c.Staff);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PinnedAsync(c.Client, c.Staff)).Should().BeEmpty();
    }

    [Fact]
    public async Task Pins_are_saved_in_the_chosen_order_and_replaced_as_a_whole()
    {
        var c = await ConsultancyAsync();
        var first = await OpenProcessAsync(c, "LO — Curitiba");
        var second = await OpenProcessAsync(c, "LO — Londrina");

        var saved = await c.Client.SendAsync(HttpMethod.Put, Url, c.Staff, new { processIds = new[] { second, first } });

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await saved.JsonAsync()).GetProperty("processIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(second, first);
        (await PinnedAsync(c.Client, c.Staff)).Should().Equal(second, first);

        await c.Client.SendAsync(HttpMethod.Put, Url, c.Staff, new { processIds = new[] { first } });
        (await PinnedAsync(c.Client, c.Staff)).Should().Equal(first);
    }

    [Fact]
    public async Task Each_person_sees_only_their_own_pins()
    {
        var c = await ConsultancyAsync();
        var other = await AnotherAnalystAsync(c);
        var process = await OpenProcessAsync(c, "LO — Curitiba");

        await c.Client.SendAsync(HttpMethod.Put, Url, c.Staff, new { processIds = new[] { process } });

        (await PinnedAsync(c.Client, other)).Should().BeEmpty();
        (await PinnedAsync(c.Client, c.Staff)).Should().Equal(process);
    }

    [Fact]
    public async Task More_than_three_or_repeated_processes_are_refused()
    {
        var c = await ConsultancyAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
            ids.Add(await OpenProcessAsync(c, $"LO {i}"));

        var tooMany = await c.Client.SendAsync(HttpMethod.Put, Url, c.Staff, new { processIds = ids });
        var repeated = await c.Client.SendAsync(HttpMethod.Put, Url, c.Staff, new { processIds = new[] { ids[0], ids[0] } });

        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooMany.JsonAsync()).GetProperty("errors").TryGetProperty("processIds", out _).Should().BeTrue();
        repeated.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await repeated.JsonAsync()).GetProperty("errors").TryGetProperty("processIds", out _).Should().BeTrue();
        (await PinnedAsync(c.Client, c.Staff)).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_processes_of_the_consultancy_can_be_pinned()
    {
        var c = await ConsultancyAsync();
        var elsewhere = await ConsultancyAsync();
        var foreign = await OpenProcessAsync(elsewhere, "LO — outra consultoria");

        var unknown = await c.Client.SendAsync(HttpMethod.Put, Url, c.Staff, new { processIds = new[] { Guid.NewGuid() } });
        var other = await c.Client.SendAsync(HttpMethod.Put, Url, c.Staff, new { processIds = new[] { foreign } });

        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.JsonAsync()).GetProperty("errors").TryGetProperty("processIds", out _).Should().BeTrue();
        other.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Saving_the_look_keeps_the_pins_and_pinning_does_not_invent_a_look()
    {
        var c = await ConsultancyAsync();
        var process = await OpenProcessAsync(c, "LO — Curitiba");
        await c.Client.SendAsync(HttpMethod.Put, Url, c.Staff, new { processIds = new[] { process } });

        // Only pins saved: the account still has no look of its own, so the app keeps the local one.
        (await c.Client.SendAsync(HttpMethod.Get, "/api/me/preferences", c.Staff)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var look = await c.Client.SendAsync(HttpMethod.Put, "/api/me/preferences", c.Staff,
            new { themeMode = "dark", themePreset = "forest", primaryColor = (string?)null, language = "en" });

        look.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PinnedAsync(c.Client, c.Staff)).Should().Equal(process);
    }

    [Fact]
    public async Task Customers_have_no_pins()
    {
        var c = await ConsultancyAsync();
        const string newPassword = "Brand-New-Pass-2";
        var contacts = new[] { new { name = "Ana", email = "ana@cliente.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var cnpj = NewCnpj();
        var customer = await (await c.Client.SendAsync(HttpMethod.Post, "/api/customers", c.Staff, new { document = cnpj, companyName = "Cliente", contacts })).JsonAsync();
        var temporary = customer.GetProperty("temporaryPassword").GetString()!;
        var pending = (await c.Client.LoginCustomerAsync(cnpj, temporary)).GetProperty("accessToken").GetString();
        var changed = await (await c.Client.SendAsync(HttpMethod.Post, "/api/auth/change-password", pending, new { currentPassword = temporary, newPassword })).JsonAsync();
        await c.Client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", changed.GetProperty("accessToken").GetString(), new { contacts });
        var token = (await c.Client.LoginCustomerAsync(cnpj, newPassword)).GetProperty("accessToken").GetString();

        var response = await c.Client.SendAsync(HttpMethod.Get, Url, token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
