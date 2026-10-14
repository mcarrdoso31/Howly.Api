using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BZ.Howly.Api.Core.Identity;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>The consultancy's own data: its Admin edits it, the sign-in screen shows its name and logo.</summary>
public class TenantProfileEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    // The smallest valid PNG header is enough: the API checks the format by its first bytes.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private record Portal(HttpClient Client, string Admin, string Analyst);

    /// <summary>A consultancy of its own: these tests change its name and logo.</summary>
    private async Task<Portal> PortalAsync()
    {
        var slug = $"prof-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Consultoria Antiga");
        var client = factory.CreatePortalClient(slug);
        var admin = NewUsername("admin");
        var analyst = NewUsername("analyst");
        await factory.CreateUserInAsync(slug, admin, Password, "Ana Admin", Roles.Admin);
        await factory.CreateUserInAsync(slug, analyst, Password, "Caio Analyst", Roles.Analyst);
        return new Portal(client, await client.LoginInternalAsync(admin), await client.LoginInternalAsync(analyst));
    }

    private static object Profile(string name = "BZ Consultoria", string? cnpj = "64.899.707/0001-64", string? state = "SP", string timeZone = "America/Sao_Paulo") => new
    {
        name,
        legalName = "BZ Consultoria Ambiental Ltda",
        cnpj,
        email = "contato@bz.example",
        phone = "(11) 91234-5678",
        postalCode = "01310-100",
        street = "Av. Paulista, 1000",
        city = "São Paulo",
        state,
        timeZone,
    };

    [Fact]
    public async Task The_admin_reads_the_profile_with_sensible_defaults()
    {
        var p = await PortalAsync();

        var profile = await (await p.Client.SendAsync(HttpMethod.Get, "/api/tenant/profile", p.Admin)).JsonAsync();

        profile.GetProperty("name").GetString().Should().Be("Consultoria Antiga");
        profile.GetProperty("timeZone").GetString().Should().Be("America/Sao_Paulo");
        profile.GetProperty("cnpj").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_admin_updates_it_and_the_portal_shows_the_new_name()
    {
        var p = await PortalAsync();

        var response = await p.Client.SendAsync(HttpMethod.Put, "/api/tenant/profile", p.Admin, Profile());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await response.JsonAsync();
        saved.GetProperty("cnpj").GetString().Should().Be("64899707000164");
        saved.GetProperty("phone").GetString().Should().Be("11912345678");
        saved.GetProperty("postalCode").GetString().Should().Be("01310100");
        (await p.Client.GetFromJsonAsync<JsonElement>("/api/tenant")).GetProperty("name").GetString().Should().Be("BZ Consultoria");
    }

    [Theory]
    [InlineData("cnpj")]
    [InlineData("state")]
    [InlineData("timeZone")]
    [InlineData("name")]
    public async Task Invalid_data_points_to_the_field(string field)
    {
        var p = await PortalAsync();
        var body = field switch
        {
            "cnpj" => Profile(cnpj: "11.111.111/1111-11"),
            "state" => Profile(state: "XX"),
            "timeZone" => Profile(timeZone: "Mars/Olympus"),
            _ => Profile(name: " "),
        };

        var response = await p.Client.SendAsync(HttpMethod.Put, "/api/tenant/profile", p.Admin, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Only_the_admin_changes_it()
    {
        var p = await PortalAsync();

        (await p.Client.SendAsync(HttpMethod.Put, "/api/tenant/profile", p.Analyst, Profile())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await p.Client.SendAsync(HttpMethod.Get, "/api/tenant/profile", p.Analyst)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_logo_shows_on_the_sign_in_screen_until_removed()
    {
        var p = await PortalAsync();
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "logo.png");
        var upload = new HttpRequestMessage(HttpMethod.Put, "/api/tenant/logo") { Content = form };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", p.Admin);

        (await p.Client.SendAsync(upload)).StatusCode.Should().Be(HttpStatusCode.OK);

        var tenant = await p.Client.GetFromJsonAsync<JsonElement>("/api/tenant");
        var version = tenant.GetProperty("logoVersion").GetString();
        version.Should().NotBeNullOrEmpty();
        var logo = await p.Client.GetAsync($"/api/tenant/logo?v={version}"); // anonymous: the sign-in screen shows it
        logo.StatusCode.Should().Be(HttpStatusCode.OK);
        logo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

        (await p.Client.SendAsync(HttpMethod.Delete, "/api/tenant/logo", p.Admin)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await p.Client.GetFromJsonAsync<JsonElement>("/api/tenant")).GetProperty("logoVersion").ValueKind.Should().Be(JsonValueKind.Null);
        (await p.Client.GetAsync("/api/tenant/logo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
