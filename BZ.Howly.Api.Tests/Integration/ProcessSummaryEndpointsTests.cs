using System.Net;
using System.Text.Json;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>The counts of the staff home page: what is late and who each open process waits on.</summary>
public class ProcessSummaryEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"9{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    [Fact]
    public async Task Counts_what_is_late_and_who_each_open_process_waits_on()
    {
        // A consultancy of its own, so the counts are this test's only.
        var slug = $"sum-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Resumo");
        var client = factory.CreatePortalClient(slug);
        var janeName = NewUsername("jane");
        var caioName = NewUsername("caio");
        await factory.CreateUserInAsync(slug, janeName, Password, "Jane Analyst", Roles.Analyst);
        var caioId = await factory.CreateUserInAsync(slug, caioName, Password, "Caio Analyst", Roles.Analyst);
        var jane = await client.LoginInternalAsync(janeName);

        var customer = await (await client.SendAsync(HttpMethod.Post, "/api/customers", jane, new
        {
            cnpj = NewCnpj(),
            companyName = "Beagle Industrial Ltda",
            contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } },
        })).JsonAsync();
        var type = await (await client.SendAsync(HttpMethod.Post, "/api/process-types", jane, new
        {
            name = "MTR",
            steps = new object[]
            {
                new { name = "Coleta", owner = "Customer", durationDays = 1 },
                new { name = "Protocolo", owner = "Staff", durationDays = 30 },
                new { name = "Análise", owner = "Agency", agencyName = "IBAMA" },
            },
        })).JsonAsync();
        async Task<Guid> Open(Guid? assigneeId = null) => (await (await client.SendAsync(HttpMethod.Post, "/api/processes", jane, new
        {
            customerId = customer.GetProperty("id").GetGuid(),
            processTypeId = type.GetProperty("id").GetGuid(),
            title = "MTR",
            assigneeId,
        })).JsonAsync()).GetProperty("id").GetGuid();

        await Open();                                                                        // with the customer
        var withStaff = await Open();
        await client.SendAsync(HttpMethod.Post, $"/api/processes/{withStaff}/advance", jane, new { });  // with the staff
        var withAgency = await Open(caioId);
        await client.SendAsync(HttpMethod.Post, $"/api/processes/{withAgency}/advance", jane, new { });
        await client.SendAsync(HttpMethod.Post, $"/api/processes/{withAgency}/advance", jane, new { }); // with the agency, Caio's
        var cancelled = await Open();
        await client.SendAsync(HttpMethod.Post, $"/api/processes/{cancelled}/cancel", jane, new { reason = "Desistiu" }); // not open

        var all = await (await client.SendAsync(HttpMethod.Get, "/api/processes/summary", jane)).JsonAsync();
        var mine = await (await client.SendAsync(HttpMethod.Get, "/api/processes/summary?mine=true", jane)).JsonAsync();

        Counts(all).Should().Equal(3, 1, 1, 1, 0);
        Counts(mine).Should().Equal(2, 1, 1, 0, 0);

        all.GetProperty("overdue").GetInt32().Should().Be(0);

        // Two days later, the customer's one-day step is late (the 30-day staff step is not).
        using var scope = factory.CreateTenantScope(slug);
        var later = await scope.ServiceProvider.GetRequiredService<IProcessRepository>().SummarizeAsync(null, DateTimeOffset.UtcNow.AddDays(2), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
        later.Overdue.Should().Be(1);
    }

    [Fact]
    public async Task Customers_have_no_staff_summary()
    {
        var response = await factory.CreateClient().GetAsync("/api/processes/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>open, waiting on customer, staff, agency, payment.</summary>
    private static int[] Counts(JsonElement summary) =>
    [
        summary.GetProperty("open").GetInt32(),
        summary.GetProperty("waitingOnCustomer").GetInt32(),
        summary.GetProperty("waitingOnStaff").GetInt32(),
        summary.GetProperty("waitingOnAgency").GetInt32(),
        summary.GetProperty("waitingOnPayment").GetInt32(),
    ];
}
