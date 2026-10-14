using System.Net;
using System.Text.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class ProcessEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"5{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private async Task<(Guid Id, string Token)> StaffAsync(string name = "Jane Analyst")
    {
        var username = NewUsername("proc");
        var id = await factory.CreateUserAsync(username, Password, name, "Analyst");
        return (id, await _client.LoginInternalAsync(username));
    }

    private async Task<Guid> CustomerAsync(string staffToken, string companyName = "Beagle Industrial Ltda") =>
        (await CustomerWithLoginAsync(staffToken, companyName, signIn: false)).Id;

    /// <summary>A customer ready to use the portal: temporary password changed and data confirmed.</summary>
    private async Task<(Guid Id, string? Token)> CustomerWithLoginAsync(string staffToken, string companyName = "Beagle Industrial Ltda", bool signIn = true)
    {
        var cnpj = NewCnpj();
        var created = await (await _client.SendAsync(HttpMethod.Post, "/api/customers", staffToken, new
        {
            cnpj,
            companyName,
            contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } },
        })).JsonAsync();
        var id = created.GetProperty("id").GetGuid();
        if (!signIn)
            return (id, null);

        var temporary = created.GetProperty("temporaryPassword").GetString()!;
        var pending = (await _client.LoginCustomerAsync(cnpj, temporary)).GetProperty("accessToken").GetString();
        var changed = await (await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", pending,
            new { currentPassword = temporary, newPassword = NewPassword })).JsonAsync();
        await _client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", changed.GetProperty("accessToken").GetString(),
            new { contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } } });
        var token = (await _client.LoginCustomerAsync(cnpj, NewPassword)).GetProperty("accessToken").GetString();
        return (id, token);
    }

    private static object Step(string name, string owner, int? durationDays = 5, bool isOptional = false, string? agencyName = null) =>
        new { name, owner, agencyName, durationDays, isOptional, customerChecklist = owner == "Customer" ? "Procuração" : null };

    private async Task<Guid> TypeAsync(string token, bool active = true)
    {
        var type = await (await _client.SendAsync(HttpMethod.Post, "/api/process-types", token, new
        {
            name = $"MTR {Guid.NewGuid():N}"[..12],
            steps = new[]
            {
                Step("Coleta de documentos", "Customer"),
                Step("Protocolo", "Staff", 3),
                Step("Pagamento da guia", "Payment", null, isOptional: true),
                Step("Análise", "Agency", 30, agencyName: "IBAMA"),
            },
        })).JsonAsync();
        var id = type.GetProperty("id").GetGuid();
        if (!active)
            await _client.SendAsync(HttpMethod.Post, $"/api/process-types/{id}/deactivate", token);
        return id;
    }

    private async Task<JsonElement> OpenAsync(string token, Guid customerId, Guid typeId, string title = "MTR — Jundiaí", Guid? parentId = null)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/processes", token, new { customerId, processTypeId = typeId, title, parentId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.JsonAsync();
    }

    private async Task<JsonElement> ActAsync(string token, Guid processId, string action, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/{action}", token, body ?? new { });
        response.StatusCode.Should().Be(expected);
        return await response.JsonAsync();
    }

    private static string[] StepStatuses(JsonElement process) =>
        process.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("status").GetString()!).ToArray();

    // ---- opening ----

    [Fact]
    public async Task Open_starts_the_first_step_and_assigns_the_opener_by_default()
    {
        var (staffId, token) = await StaffAsync();
        var process = await OpenAsync(token, await CustomerAsync(token), await TypeAsync(token));

        process.GetProperty("title").GetString().Should().Be("MTR — Jundiaí");
        process.GetProperty("status").GetString().Should().Be("InProgress");
        process.GetProperty("waitingOn").GetString().Should().Be("Customer");
        process.GetProperty("customer").GetProperty("name").GetString().Should().Be("Beagle Industrial Ltda");
        process.GetProperty("assignee").GetProperty("id").GetGuid().Should().Be(staffId);
        process.GetProperty("assignee").GetProperty("name").GetString().Should().Be("Jane Analyst");
        StepStatuses(process).Should().Equal("Active", "Pending", "Pending", "Pending");
        process.GetProperty("steps")[3].GetProperty("agencyName").GetString().Should().Be("IBAMA");
        process.GetProperty("events")[0].GetProperty("type").GetString().Should().Be("Opened");
    }

    [Fact]
    public async Task Open_rejects_inactive_types_unknown_customers_and_blank_titles()
    {
        var (_, token) = await StaffAsync();
        var customer = await CustomerAsync(token);

        async Task<JsonElement> ErrorsAsync(object body)
        {
            var response = await _client.SendAsync(HttpMethod.Post, "/api/processes", token, body);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            return (await response.JsonAsync()).GetProperty("errors");
        }

        (await ErrorsAsync(new { customerId = customer, processTypeId = await TypeAsync(token, active: false), title = "MTR" }))
            .TryGetProperty("processTypeId", out _).Should().BeTrue();
        (await ErrorsAsync(new { customerId = Guid.NewGuid(), processTypeId = await TypeAsync(token), title = "MTR" }))
            .TryGetProperty("customerId", out _).Should().BeTrue();
        (await ErrorsAsync(new { customerId = customer, processTypeId = await TypeAsync(token), title = " " }))
            .TryGetProperty("title", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Open_assigns_another_staff_member_when_asked()
    {
        var (_, token) = await StaffAsync();
        var (otherId, _) = await StaffAsync("Caio Admin");

        var response = await _client.SendAsync(HttpMethod.Post, "/api/processes", token,
            new { customerId = await CustomerAsync(token), processTypeId = await TypeAsync(token), title = "MTR", assigneeId = otherId });

        (await response.JsonAsync()).GetProperty("assignee").GetProperty("name").GetString().Should().Be("Caio Admin");
    }

    // ---- moving the steps ----

    [Fact]
    public async Task Steps_advance_skip_return_and_complete()
    {
        var (_, token) = await StaffAsync();
        var id = (await OpenAsync(token, await CustomerAsync(token), await TypeAsync(token))).GetProperty("id").GetGuid();

        var advanced = await ActAsync(token, id, "advance", new { comment = "Documentos ok" });
        StepStatuses(advanced).Should().Equal("Done", "Active", "Pending", "Pending");
        advanced.GetProperty("waitingOn").GetString().Should().Be("Staff");

        await ActAsync(token, id, "skip", null, HttpStatusCode.Conflict); // "Protocolo" is required
        await ActAsync(token, id, "advance");
        var skipped = await ActAsync(token, id, "skip", new { comment = "Isento" });
        StepStatuses(skipped).Should().Equal("Done", "Done", "Skipped", "Active");
        skipped.GetProperty("waitingOn").GetString().Should().Be("Agency");

        var first = skipped.GetProperty("steps")[0].GetProperty("id").GetGuid();
        await ActAsync(token, id, "return", new { stepId = first }, HttpStatusCode.BadRequest); // comment required
        var returned = await ActAsync(token, id, "return", new { stepId = first, comment = "Procuração vencida" });
        StepStatuses(returned).Should().Equal("Active", "Pending", "Pending", "Pending");

        for (var i = 0; i < 4; i++)
            await ActAsync(token, id, "advance");
        var completed = await (await _client.SendAsync(HttpMethod.Get, $"/api/processes/{id}", token)).JsonAsync();
        completed.GetProperty("status").GetString().Should().Be("Completed");
        completed.GetProperty("waitingOn").ValueKind.Should().Be(JsonValueKind.Null);
        completed.GetProperty("events").EnumerateArray().Last().GetProperty("type").GetString().Should().Be("Completed");
    }

    [Fact]
    public async Task Pause_resume_cancel_and_comment()
    {
        var (_, token) = await StaffAsync();
        var id = (await OpenAsync(token, await CustomerAsync(token), await TypeAsync(token))).GetProperty("id").GetGuid();

        await ActAsync(token, id, "pause", new { }, HttpStatusCode.BadRequest); // reason required
        var paused = await ActAsync(token, id, "pause", new { reason = "Aguardando definição da unidade" });
        paused.GetProperty("status").GetString().Should().Be("Paused");
        paused.GetProperty("statusReason").GetString().Should().Be("Aguardando definição da unidade");
        await ActAsync(token, id, "advance", null, HttpStatusCode.Conflict);
        var commented = await ActAsync(token, id, "comments", new { comment = "Aguardando o cliente" });
        commented.GetProperty("events").EnumerateArray().Last().GetProperty("comment").GetString().Should().Be("Aguardando o cliente");

        (await ActAsync(token, id, "resume")).GetProperty("status").GetString().Should().Be("InProgress");
        (await ActAsync(token, id, "cancel", new { reason = "Cliente desistiu" })).GetProperty("status").GetString().Should().Be("Cancelled");
        await ActAsync(token, id, "resume", null, HttpStatusCode.Conflict);
        await ActAsync(token, id, "comments", new { comment = "Arquivado" }, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Assign_changes_the_responsible_staff_member()
    {
        var (_, token) = await StaffAsync();
        var (otherId, _) = await StaffAsync("Caio Admin");
        var id = (await OpenAsync(token, await CustomerAsync(token), await TypeAsync(token))).GetProperty("id").GetGuid();

        var assigned = await ActAsync(token, id, "assign", new { assigneeId = otherId });

        assigned.GetProperty("assignee").GetProperty("id").GetGuid().Should().Be(otherId);
        await ActAsync(token, id, "assign", new { assigneeId = Guid.NewGuid() }, HttpStatusCode.BadRequest);
    }

    // ---- lists ----

    [Fact]
    public async Task List_filters_by_who_the_process_waits_on_status_assignee_and_customer()
    {
        var (staffId, token) = await StaffAsync();
        var customer = await CustomerAsync(token, $"Filter {Guid.NewGuid():N}"[..20]);
        var type = await TypeAsync(token);
        var atCustomer = (await OpenAsync(token, customer, type, "A")).GetProperty("id").GetGuid();
        var atStaff = (await OpenAsync(token, customer, type, "B")).GetProperty("id").GetGuid();
        await ActAsync(token, atStaff, "advance");
        var paused = (await OpenAsync(token, customer, type, "C")).GetProperty("id").GetGuid();
        await ActAsync(token, paused, "pause", new { reason = "x" });

        async Task<Guid[]> IdsAsync(string query) =>
            (await (await _client.SendAsync(HttpMethod.Get, $"/api/processes?customerId={customer}{query}", token)).JsonAsync())
                .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray();

        (await IdsAsync("")).Should().BeEquivalentTo([atCustomer, atStaff, paused]);
        (await IdsAsync("&waitingOn=Customer")).Should().Equal(atCustomer);
        (await IdsAsync("&waitingOn=Staff")).Should().Equal(atStaff);
        (await IdsAsync("&status=Paused")).Should().Equal(paused);
        (await IdsAsync($"&assigneeId={staffId}")).Should().HaveCount(3);
        (await IdsAsync($"&assigneeId={Guid.NewGuid()}")).Should().BeEmpty();
        (await IdsAsync("&overdue=true")).Should().BeEmpty(); // fresh steps are within their deadline
    }

    [Fact]
    public async Task List_items_carry_what_the_screens_show()
    {
        var (_, token) = await StaffAsync();
        var customer = await CustomerAsync(token);
        var id = (await OpenAsync(token, customer, await TypeAsync(token), "MTR Itens")).GetProperty("id").GetGuid();
        await ActAsync(token, id, "advance");

        var item = (await (await _client.SendAsync(HttpMethod.Get, $"/api/processes?customerId={customer}", token)).JsonAsync())
            .GetProperty("items")[0];

        item.GetProperty("title").GetString().Should().Be("MTR Itens");
        item.GetProperty("customerName").GetString().Should().Be("Beagle Industrial Ltda");
        item.GetProperty("assigneeName").GetString().Should().Be("Jane Analyst");
        item.GetProperty("activeStepName").GetString().Should().Be("Protocolo");
        item.GetProperty("waitingOn").GetString().Should().Be("Staff");
        item.GetProperty("stepsDone").GetInt32().Should().Be(1);
        item.GetProperty("stepsTotal").GetInt32().Should().Be(4);
        item.GetProperty("activeStepDueAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    // ---- subprocesses ----

    [Fact]
    public async Task Subprocesses_belong_to_a_parent_of_the_same_customer()
    {
        var (_, token) = await StaffAsync();
        var customer = await CustomerAsync(token);
        var type = await TypeAsync(token);
        var parent = (await OpenAsync(token, customer, type, "PGRS 2026")).GetProperty("id").GetGuid();

        var child = await OpenAsync(token, customer, type, "Licença de operação", parent);
        var done = (await OpenAsync(token, customer, type, "Cadastro técnico", parent)).GetProperty("id").GetGuid();
        await ActAsync(token, done, "cancel", new { reason = "Não se aplica" });

        child.GetProperty("parent").GetProperty("title").GetString().Should().Be("PGRS 2026");
        var parentBody = await (await _client.SendAsync(HttpMethod.Get, $"/api/processes/{parent}", token)).JsonAsync();
        parentBody.GetProperty("children").EnumerateArray().Select(c => c.GetProperty("title").GetString())
            .Should().BeEquivalentTo(["Licença de operação", "Cadastro técnico"]);

        var otherCustomer = await CustomerAsync(token, "Outra Ltda");
        var wrong = await _client.SendAsync(HttpMethod.Post, "/api/processes", token,
            new { customerId = otherCustomer, processTypeId = type, title = "X", parentId = parent });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.JsonAsync()).GetProperty("errors").TryGetProperty("parentId", out _).Should().BeTrue();

        var grandchild = await _client.SendAsync(HttpMethod.Post, "/api/processes", token,
            new { customerId = customer, processTypeId = type, title = "Y", parentId = child.GetProperty("id").GetGuid() });
        grandchild.StatusCode.Should().Be(HttpStatusCode.BadRequest); // one level only
    }

    // ---- customers ----

    [Fact]
    public async Task Customers_follow_only_their_own_processes()
    {
        var (_, staffToken) = await StaffAsync();
        var (customerId, customerToken) = await CustomerWithLoginAsync(staffToken);
        var other = await CustomerAsync(staffToken, "Outra Ltda");
        var type = await TypeAsync(staffToken);
        var mine = (await OpenAsync(staffToken, customerId, type, "Meu MTR")).GetProperty("id").GetGuid();
        var theirs = (await OpenAsync(staffToken, other, type, "MTR de outro")).GetProperty("id").GetGuid();
        await ActAsync(staffToken, mine, "comments", new { comment = "Recebemos a procuração" });

        var list = await (await _client.SendAsync(HttpMethod.Get, "/api/me/processes", customerToken)).JsonAsync();
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(mine);

        var detail = await (await _client.SendAsync(HttpMethod.Get, $"/api/me/processes/{mine}", customerToken)).JsonAsync();
        detail.GetProperty("steps")[0].GetProperty("customerChecklist").GetString().Should().Be("Procuração");
        detail.GetProperty("events").EnumerateArray().Last().GetProperty("comment").GetString().Should().Be("Recebemos a procuração");

        (await _client.SendAsync(HttpMethod.Get, $"/api/me/processes/{theirs}", customerToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.SendAsync(HttpMethod.Get, "/api/processes", customerToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.SendAsync(HttpMethod.Post, $"/api/processes/{mine}/advance", customerToken, new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
