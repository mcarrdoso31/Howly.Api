using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>Rules decided on 2026-10-14, through the API.</summary>
public class ProcessRulesEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";
    private const string License = "Licença de operação";
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nfictitious test content\n%%EOF");

    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"7{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Scenario(Guid StaffId, string StaffToken, Guid CustomerId, string CustomerToken, Guid TypeId, Guid ProcessId, JsonElement Process);

    /// <summary>Customer step asking for a license, then a staff step, then the agency.</summary>
    private async Task<Scenario> ScenarioAsync()
    {
        var username = NewUsername("rules");
        var staffId = await factory.CreateUserAsync(username, Password, "Jane Analyst", "Analyst");
        var staff = await _client.LoginInternalAsync(username);

        var contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var cnpj = NewCnpj();
        var customer = await (await _client.SendAsync(HttpMethod.Post, "/api/customers", staff, new { document = cnpj, companyName = "Beagle Industrial Ltda", contacts })).JsonAsync();
        var temporary = customer.GetProperty("temporaryPassword").GetString()!;
        var pending = (await _client.LoginCustomerAsync(cnpj, temporary)).GetProperty("accessToken").GetString();
        var changed = await (await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", pending, new { currentPassword = temporary, newPassword = NewPassword })).JsonAsync();
        await _client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", changed.GetProperty("accessToken").GetString(), new { contacts });
        var customerToken = (await _client.LoginCustomerAsync(cnpj, NewPassword)).GetProperty("accessToken").GetString()!;

        var type = await (await _client.SendAsync(HttpMethod.Post, "/api/process-types", staff, new
        {
            name = $"MTR {Guid.NewGuid():N}"[..12],
            steps = new object[]
            {
                new { name = "Coleta", owner = "Customer", durationDays = 5, documents = new[] { new { name = License, isRequired = true } }, deliverables = Array.Empty<string>() },
                new { name = "Protocolo", owner = "Staff", durationDays = 3, documents = Array.Empty<object>(), deliverables = Array.Empty<string>() },
                new { name = "Emissão", owner = "Agency", agencyName = "IBAMA", documents = Array.Empty<object>(), deliverables = new[] { "Licença" } },
            },
        })).JsonAsync();
        var typeId = type.GetProperty("id").GetGuid();
        var customerId = customer.GetProperty("id").GetGuid();

        var process = await OpenAsync(staff, customerId, typeId);
        return new Scenario(staffId, staff, customerId, customerToken, typeId, process.GetProperty("id").GetGuid(), process);
    }

    private async Task<JsonElement> OpenAsync(string token, Guid customerId, Guid typeId, Guid? parentId = null, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/processes", token, new { customerId, processTypeId = typeId, title = "MTR — Jundiaí", parentId });
        response.StatusCode.Should().Be(expected);
        return await response.JsonAsync();
    }

    private async Task<JsonElement> ActAsync(string token, Guid processId, string action, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _client.SendAsync(HttpMethod.Post, $"/api/processes/{processId}/{action}", token, body ?? new { });
        response.StatusCode.Should().Be(expected);
        return await response.JsonAsync();
    }

    private static JsonElement Document(JsonElement process, string name) =>
        process.GetProperty("documents").EnumerateArray().Single(d => d.GetProperty("name").GetString() == name);

    private Task<HttpResponseMessage> UploadAsync(string token, string url, Dictionary<string, string>? fields = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "licenca.pdf");
        foreach (var (key, value) in fields ?? [])
            form.Add(new StringContent(value), key);

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> CustomerUploadAsync(Scenario s) =>
        UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{Document(s.Process, License).GetProperty("id").GetGuid()}/versions");

    // ---- the customer only sends files on their own step ----

    [Fact]
    public async Task The_customer_sends_files_only_while_the_step_is_theirs()
    {
        var s = await ScenarioAsync();

        (await CustomerUploadAsync(s)).StatusCode.Should().Be(HttpStatusCode.OK);
        await ActAsync(s.StaffToken, s.ProcessId, "advance");

        (await CustomerUploadAsync(s)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents", new() { ["name"] = "Outro" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.SendAsync(HttpMethod.Post, $"/api/me/processes/{s.ProcessId}/comments", s.CustomerToken, new { comment = "Alguma novidade?" }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "comments stay open while the process is open");
    }

    [Fact]
    public async Task Only_the_staff_reopen_an_approved_document()
    {
        var s = await ScenarioAsync();
        var license = Document(s.Process, License).GetProperty("id").GetGuid();
        await CustomerUploadAsync(s);
        await ActAsync(s.StaffToken, s.ProcessId, $"documents/{license}/approve");

        (await CustomerUploadAsync(s)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var reopened = await ActAsync(s.StaffToken, s.ProcessId, $"documents/{license}/reopen");

        Document(reopened, License).GetProperty("status").GetString().Should().Be("InReview");
        reopened.GetProperty("events").EnumerateArray().Last().GetProperty("type").GetString().Should().Be("DocumentReopened");
        (await _client.SendAsync(HttpMethod.Post, $"/api/me/processes/{s.ProcessId}/documents/{license}/reopen", s.CustomerToken, new { }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "there is no such route for customers");
    }

    // ---- pause freezes the process ----

    [Fact]
    public async Task A_paused_process_takes_no_document_changes_but_takes_comments()
    {
        var s = await ScenarioAsync();
        var license = Document(s.Process, License).GetProperty("id").GetGuid();
        await CustomerUploadAsync(s);
        await ActAsync(s.StaffToken, s.ProcessId, "pause", new { reason = "Cliente pediu para segurar" });

        await ActAsync(s.StaffToken, s.ProcessId, $"documents/{license}/approve", expected: HttpStatusCode.Conflict);
        (await CustomerUploadAsync(s)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await UploadAsync(s.StaffToken, $"/api/processes/{s.ProcessId}/documents", new() { ["name"] = "Ofício" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.SendAsync(HttpMethod.Post, $"/api/me/processes/{s.ProcessId}/comments", s.CustomerToken, new { comment = "Ok, aguardo" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Finished_processes_take_no_review()
    {
        var s = await ScenarioAsync();
        var license = Document(s.Process, License).GetProperty("id").GetGuid();
        await CustomerUploadAsync(s);
        await ActAsync(s.StaffToken, s.ProcessId, "cancel", new { reason = "Desistiu" });

        await ActAsync(s.StaffToken, s.ProcessId, $"documents/{license}/approve", expected: HttpStatusCode.Conflict);
        await ActAsync(s.StaffToken, s.ProcessId, $"documents/{license}/reject", new { reason = "Vencido" }, HttpStatusCode.Conflict);
    }

    // ---- documents left behind ----

    [Fact]
    public async Task Documents_not_sent_are_waived_when_the_step_ends_and_asked_again_on_return()
    {
        var s = await ScenarioAsync();

        var advanced = await ActAsync(s.StaffToken, s.ProcessId, "advance");
        Document(advanced, License).GetProperty("status").GetString().Should().Be("Waived");
        advanced.GetProperty("events").EnumerateArray().Last(e => e.GetProperty("type").GetString()!.StartsWith("StepAdvanced"))
            .GetProperty("details").GetString().Should().Be(License);

        var firstStep = s.Process.GetProperty("steps")[0].GetProperty("id").GetGuid();
        var returned = await ActAsync(s.StaffToken, s.ProcessId, "return", new { stepId = firstStep, comment = "Faltou a licença" });
        Document(returned, License).GetProperty("status").GetString().Should().Be("AwaitingUpload");
    }

    [Fact]
    public async Task Staff_extras_are_the_default_kind()
    {
        var s = await ScenarioAsync();

        var response = await UploadAsync(s.StaffToken, $"/api/processes/{s.ProcessId}/documents", new() { ["name"] = "Ofício do órgão" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Document(await response.JsonAsync(), "Ofício do órgão").GetProperty("kind").GetString().Should().Be("Extra");
    }

    // ---- subprocesses ----

    [Fact]
    public async Task Subprocesses_need_an_open_parent_and_hold_it_open()
    {
        var s = await ScenarioAsync();
        var child = await OpenAsync(s.StaffToken, s.CustomerId, s.TypeId, s.ProcessId);

        await ActAsync(s.StaffToken, s.ProcessId, "cancel", new { reason = "Desistiu" }, HttpStatusCode.Conflict);
        await ActAsync(s.StaffToken, child.GetProperty("id").GetGuid(), "cancel", new { reason = "Desistiu" });
        await ActAsync(s.StaffToken, s.ProcessId, "pause", new { reason = "Pausa" });

        await OpenAsync(s.StaffToken, s.CustomerId, s.TypeId, s.ProcessId, HttpStatusCode.Conflict);
        await ActAsync(s.StaffToken, s.ProcessId, "cancel", new { reason = "Desistiu" });
    }

    // ---- assign ----

    [Fact]
    public async Task The_previous_assignee_is_told_when_the_process_changes_hands()
    {
        var s = await ScenarioAsync();
        var otherName = NewUsername("other");
        var otherId = await factory.CreateUserAsync(otherName, Password, "Caio Analyst", "Analyst");
        var other = await _client.LoginInternalAsync(otherName);

        await ActAsync(other, s.ProcessId, "assign", new { assigneeId = otherId });

        var bell = await (await _client.SendAsync(HttpMethod.Get, "/api/me/notifications", s.StaffToken)).JsonAsync();
        bell.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()).Should().Contain("Assigned");
        var events = (await ActAsync(other, s.ProcessId, "assign", new { assigneeId = otherId })).GetProperty("events").GetArrayLength();
        (await ActAsync(other, s.ProcessId, "assign", new { assigneeId = otherId })).GetProperty("events").GetArrayLength().Should().Be(events);
    }

    // ---- staff list ----

    [Fact]
    public async Task The_staff_list_leaves_out_inactive_people()
    {
        var adminName = NewUsername("admin");
        await factory.CreateUserAsync(adminName, Password, "Ana Admin", "Admin");
        var admin = await _client.LoginInternalAsync(adminName);
        var goneId = await factory.CreateUserAsync(NewUsername("gone"), Password, "Gil Gone", "Analyst");
        await _client.SendAsync(HttpMethod.Post, $"/api/internal-users/{goneId}/deactivate", admin, new { });

        var staff = await (await _client.SendAsync(HttpMethod.Get, "/api/staff", admin)).JsonAsync();

        var ids = staff.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        ids.Should().NotContain(goneId);
        staff.EnumerateArray().Select(p => p.GetProperty("name").GetString()).Should().Contain("Ana Admin");
    }

    // ---- process types ----

    [Fact]
    public async Task Types_need_the_owner_but_not_the_agency()
    {
        var s = await ScenarioAsync();

        (await _client.SendAsync(HttpMethod.Post, "/api/process-types", s.StaffToken, new { name = "Sem dono", steps = new[] { new { name = "Coleta" } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var noAgency = await _client.SendAsync(HttpMethod.Post, "/api/process-types", s.StaffToken,
            new { name = "Sem órgão", steps = new[] { new { name = "Análise", owner = "Agency" } } });
        noAgency.StatusCode.Should().Be(HttpStatusCode.Created, "each process can set the agency of the step");
    }

    // ---- concurrency ----

    [Fact]
    public async Task Saving_over_a_process_someone_else_changed_is_a_conflict()
    {
        var s = await ScenarioAsync();
        using var first = factory.CreateTenantScope();
        using var second = factory.CreateTenantScope();
        var mine = await first.ServiceProvider.GetRequiredService<IProcessRepository>().FindByIdAsync(s.ProcessId);
        var theirs = await second.ServiceProvider.GetRequiredService<IProcessRepository>().FindByIdAsync(s.ProcessId);
        var actor = new Actor(s.StaffId, "Jane Analyst");

        theirs!.Comment(actor, "Salvo primeiro", DateTimeOffset.UtcNow);
        await second.ServiceProvider.GetRequiredService<IProcessRepository>().SaveChangesAsync();
        mine!.Comment(actor, "Salvo por cima", DateTimeOffset.UtcNow);
        var save = () => first.ServiceProvider.GetRequiredService<IProcessRepository>().SaveChangesAsync();

        (await save.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("concurrency_conflict");
    }
}
