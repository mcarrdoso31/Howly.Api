using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Core.Tenancy;
using BZ.Howly.Api.DAO.Configurations;
using BZ.Howly.Api.Workers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>One database, many consultancies: nothing of one ever reaches another.</summary>
public class TenantIsolationTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nfictitious test content\n%%EOF");

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"8{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private static string NewSlug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    /// <summary>A consultancy with its portal client and an analyst signed in.</summary>
    private record Portal(Tenant Tenant, HttpClient Client, Guid StaffId, string StaffToken);

    private async Task<Portal> PortalAsync(string slug)
    {
        var tenant = slug == TenantConfiguration.BiaSlug ? Bia() : await factory.CreateTenantAsync(slug, $"Consultoria {slug}");
        var client = factory.CreatePortalClient(slug);
        var username = NewUsername("iso");
        var staffId = await factory.CreateUserInAsync(slug, username, Password, "Jane Analyst", Roles.Analyst);
        return new Portal(tenant, client, staffId, await client.LoginInternalAsync(username));
    }

    private Tenant Bia()
    {
        using var scope = factory.CreateTenantScope();
        return scope.ServiceProvider.GetRequiredService<ITenantContext>().Tenant!;
    }

    private static async Task<JsonElement> CreateCustomerAsync(Portal portal, string cnpj)
    {
        var response = await portal.Client.SendAsync(HttpMethod.Post, "/api/customers", portal.StaffToken, new
        {
            cnpj,
            companyName = "Beagle Industrial Ltda",
            contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.JsonAsync();
    }

    private static async Task<(Guid TypeId, Guid ProcessId, JsonElement Process)> CreateProcessAsync(Portal portal, Guid customerId, int durationDays = 5)
    {
        var type = await (await portal.Client.SendAsync(HttpMethod.Post, "/api/process-types", portal.StaffToken, new
        {
            name = $"MTR {Guid.NewGuid():N}"[..12],
            steps = new object[]
            {
                new { name = "Coleta", owner = "Customer", durationDays, documents = new[] { new { name = "Licença", isRequired = true } }, deliverables = Array.Empty<string>() },
            },
        })).JsonAsync();
        var process = await (await portal.Client.SendAsync(HttpMethod.Post, "/api/processes", portal.StaffToken,
            new { customerId, processTypeId = type.GetProperty("id").GetGuid(), title = "MTR — Jundiaí" })).JsonAsync();
        return (type.GetProperty("id").GetGuid(), process.GetProperty("id").GetGuid(), process);
    }

    // ---- finding the portal ----

    [Fact]
    public async Task An_unknown_portal_does_not_exist()
    {
        var response = await factory.CreatePortalClient("nobody-here").GetAsync("/api/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.JsonAsync()).GetProperty("code").GetString().Should().Be("tenant_not_found");
    }

    [Fact]
    public async Task A_suspended_portal_refuses_everyone()
    {
        var slug = NewSlug("susp");
        await factory.CreateTenantAsync(slug, "Suspensa", TenantStatus.Suspended);

        var response = await factory.CreatePortalClient(slug).GetAsync("/api/tenant");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.JsonAsync()).GetProperty("code").GetString().Should().Be("tenant_suspended");
    }

    [Fact]
    public async Task Each_portal_tells_its_own_name()
    {
        var slug = NewSlug("name");
        await factory.CreateTenantAsync(slug, "Claude Consultoria");

        var bia = await factory.CreatePortalClient(TenantConfiguration.BiaSlug).GetFromJsonAsync<JsonElement>("/api/tenant");
        var other = await factory.CreatePortalClient(slug).GetFromJsonAsync<JsonElement>("/api/tenant");

        bia.GetProperty("name").GetString().Should().Be("Bia Consultoria Ambiental");
        other.GetProperty("name").GetString().Should().Be("Claude Consultoria");
        other.GetProperty("isPlatform").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_portal_created_after_a_miss_opens_once_the_short_miss_cache_expires()
    {
        var slug = NewSlug("late");
        (await factory.CreatePortalClient(slug).GetAsync("/api/tenant")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await factory.CreateTenantAsync(slug, "Chegou depois");

        // The tests keep misses for no time at all; production keeps them for seconds, not minutes.
        (await factory.CreatePortalClient(slug).GetAsync("/api/tenant")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_health_check_needs_no_portal()
    {
        var response = await factory.CreatePortalClient("nobody-here").GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- data never crosses ----

    [Fact]
    public async Task A_consultancy_sees_nothing_of_another()
    {
        var bia = await PortalAsync(TenantConfiguration.BiaSlug);
        var other = await PortalAsync(NewSlug("other"));
        var customer = await CreateCustomerAsync(bia, NewCnpj());
        var customerId = customer.GetProperty("id").GetGuid();
        var (typeId, processId, process) = await CreateProcessAsync(bia, customerId);
        var documentId = process.GetProperty("documents")[0].GetProperty("id").GetGuid();

        var customers = await (await other.Client.SendAsync(HttpMethod.Get, "/api/customers", other.StaffToken)).JsonAsync();
        var processes = await (await other.Client.SendAsync(HttpMethod.Get, "/api/processes", other.StaffToken)).JsonAsync();
        var types = await (await other.Client.SendAsync(HttpMethod.Get, "/api/process-types?includeInactive=true", other.StaffToken)).JsonAsync();
        var staff = await (await other.Client.SendAsync(HttpMethod.Get, "/api/staff", other.StaffToken)).JsonAsync();

        customers.GetProperty("totalCount").GetInt32().Should().Be(0);
        processes.GetProperty("totalCount").GetInt32().Should().Be(0);
        types.EnumerateArray().Should().BeEmpty();
        staff.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Should().NotContain(bia.StaffId);
        foreach (var url in new[]
                 {
                     $"/api/customers/{customerId}", $"/api/processes/{processId}", $"/api/process-types/{typeId}",
                 })
            (await other.Client.SendAsync(HttpMethod.Get, url, other.StaffToken)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        (await other.Client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/comments", other.StaffToken, new { comment = "oi" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.Client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/assign", bia.StaffToken, new { assigneeId = bia.StaffId }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a token only works on its own portal");
        (await other.Client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/documents/{documentId}/approve", other.StaffToken, new { }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task People_of_another_consultancy_cannot_be_assigned()
    {
        var bia = await PortalAsync(TenantConfiguration.BiaSlug);
        var other = await PortalAsync(NewSlug("assign"));
        var (_, processId, _) = await CreateProcessAsync(bia, (await CreateCustomerAsync(bia, NewCnpj())).GetProperty("id").GetGuid());

        var response = await bia.Client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/assign", bia.StaffToken, new { assigneeId = other.StaffId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_same_company_is_a_separate_customer_in_each_consultancy()
    {
        var bia = await PortalAsync(TenantConfiguration.BiaSlug);
        var other = await PortalAsync(NewSlug("cnpj"));
        var cnpj = NewCnpj();

        var inBia = await CreateCustomerAsync(bia, cnpj);
        var inOther = await CreateCustomerAsync(other, cnpj);

        inBia.GetProperty("id").GetGuid().Should().NotBe(inOther.GetProperty("id").GetGuid());
        var biaPassword = inBia.GetProperty("temporaryPassword").GetString()!;
        var otherPassword = inOther.GetProperty("temporaryPassword").GetString()!;
        (await bia.Client.PostAsJsonAsync("/api/auth/customer/login", new { cnpj, password = biaPassword })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await other.Client.PostAsJsonAsync("/api/auth/customer/login", new { cnpj, password = otherPassword })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await other.Client.PostAsJsonAsync("/api/auth/customer/login", new { cnpj, password = biaPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "each consultancy has its own account and password");
    }

    [Fact]
    public async Task An_analyst_signs_in_only_on_their_own_portal()
    {
        var other = await PortalAsync(NewSlug("login"));
        var username = NewUsername("only");
        await factory.CreateUserAsync(username, Password, "Bia Analyst", Roles.Analyst);

        var response = await other.Client.PostAsJsonAsync("/api/auth/internal/login", new { username, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- the platform portal ----

    [Fact]
    public async Task The_platform_portal_has_no_business_routes()
    {
        var client = factory.CreatePortalClient(Tenant.PlatformSlug);
        var username = NewUsername("owner");
        await factory.CreateUserInAsync(Tenant.PlatformSlug, username, Password, "Howly Owner", Roles.PlatformAdmin);

        var token = await client.LoginInternalAsync(username);

        (await client.SendAsync(HttpMethod.Get, "/api/me/profile", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.SendAsync(HttpMethod.Get, "/api/customers", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.SendAsync(HttpMethod.Get, "/api/processes", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<JsonElement>("/api/tenant")).GetProperty("isPlatform").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Platform_owners_do_not_sign_in_on_consultancy_portals()
    {
        var username = NewUsername("owner2");
        await factory.CreateUserAsync(username, Password, "Misplaced Owner", Roles.PlatformAdmin);

        var response = await factory.CreatePortalClient(TenantConfiguration.BiaSlug)
            .PostAsJsonAsync("/api/auth/internal/login", new { username, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- writing ----

    [Fact]
    public async Task Tenant_data_cannot_be_written_without_a_tenant()
    {
        using var scope = factory.Services.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
        await customers.AddAsync(Customer.Create(
            Cnpj.Parse(NewCnpj()), "Sem consultoria", [new ContactDetails("Bia", "bia@beagle.example", "11912345678", true)], Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));

        var save = () => customers.SaveChangesAsync();

        await save.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Files_are_kept_apart_per_consultancy()
    {
        var other = await PortalAsync(NewSlug("files"));
        var (_, processId, process) = await CreateProcessAsync(other, (await CreateCustomerAsync(other, NewCnpj())).GetProperty("id").GetGuid());
        var documentId = process.GetProperty("documents")[0].GetProperty("id").GetGuid();
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "licenca.pdf");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/processes/{processId}/documents/{documentId}/versions") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", other.StaffToken);

        (await other.Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        Directory.GetFiles(Path.Combine(factory.FilesRoot, other.Tenant.Id.ToString("N"), "documents")).Should().ContainSingle();
    }

    // ---- background work ----

    [Fact]
    public async Task The_overdue_check_goes_through_every_consultancy()
    {
        var bia = await PortalAsync(TenantConfiguration.BiaSlug);
        var other = await PortalAsync(NewSlug("late"));
        var (_, biaProcess, _) = await CreateProcessAsync(bia, (await CreateCustomerAsync(bia, NewCnpj())).GetProperty("id").GetGuid(), durationDays: 1);
        var (_, otherProcess, _) = await CreateProcessAsync(other, (await CreateCustomerAsync(other, NewCnpj())).GetProperty("id").GetGuid(), durationDays: 1);

        await factory.Services.GetRequiredService<OverdueStepsRunner>().RunOnceAsync(DateTimeOffset.UtcNow.AddDays(3));

        async Task<string?> LastEvent(Portal portal, Guid processId) =>
            (await (await portal.Client.SendAsync(HttpMethod.Get, $"/api/processes/{processId}", portal.StaffToken)).JsonAsync())
            .GetProperty("events").EnumerateArray().Last().GetProperty("type").GetString();
        (await LastEvent(bia, biaProcess)).Should().Be("StepOverdue");
        (await LastEvent(other, otherProcess)).Should().Be("StepOverdue");
    }

    [Fact]
    public async Task The_overdue_check_runs_once_per_interval_whatever_the_number_of_instances()
    {
        var portal = await PortalAsync(NewSlug("round"));
        var runner = factory.Services.GetRequiredService<OverdueStepsRunner>();
        var interval = TimeSpan.FromHours(1);
        var start = DateTimeOffset.UtcNow.AddDays(30); // after every round the other tests may have recorded
        await runner.RunOnceAsync(start, interval);
        var (_, processId, _) = await CreateProcessAsync(portal, (await CreateCustomerAsync(portal, NewCnpj())).GetProperty("id").GetGuid(), durationDays: 1);

        // Another instance ticking a few minutes later: this interval's round already happened.
        await runner.RunOnceAsync(start.AddMinutes(10), interval);
        var events = (await (await portal.Client.SendAsync(HttpMethod.Get, $"/api/processes/{processId}", portal.StaffToken)).JsonAsync()).GetProperty("events");
        events.EnumerateArray().Select(e => e.GetProperty("type").GetString()).Should().NotContain("StepOverdue");

        await runner.RunOnceAsync(start.AddHours(1), interval);
        events = (await (await portal.Client.SendAsync(HttpMethod.Get, $"/api/processes/{processId}", portal.StaffToken)).JsonAsync()).GetProperty("events");
        events.EnumerateArray().Last().GetProperty("type").GetString().Should().Be("StepOverdue");
    }
}
