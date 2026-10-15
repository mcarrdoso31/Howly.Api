using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BZ.Howly.Api.Core.Interfaces.Service;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class NotificationEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"7{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Scenario(string Staff, string Customer, Guid ProcessId, JsonElement Process);

    private async Task<(Guid Id, string Token)> StaffAsync(string name = "Jane Analyst")
    {
        var username = NewUsername("notif");
        var id = await factory.CreateUserAsync(username, Password, name, "Analyst");
        return (id, await _client.LoginInternalAsync(username));
    }

    /// <summary>An analyst, a customer with portal access and a process of that customer assigned to the analyst.</summary>
    private async Task<Scenario> ScenarioAsync(int? collectDays = null)
    {
        var (_, staff) = await StaffAsync();
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
                new { name = "Coleta", owner = "Customer", durationDays = collectDays, isOptional = false, documents = new[] { new { name = "Procuração", isRequired = true } } },
                new { name = "Análise", owner = "Agency", agencyName = "IBAMA", isOptional = false },
            },
        })).JsonAsync();
        var process = await (await _client.SendAsync(HttpMethod.Post, "/api/processes", staff,
            new { customerId = customer.GetProperty("id").GetGuid(), processTypeId = type.GetProperty("id").GetGuid(), title = "MTR — Jundiaí" })).JsonAsync();
        return new Scenario(staff, customerToken, process.GetProperty("id").GetGuid(), process);
    }

    private async Task<JsonElement> NotificationsAsync(string token, string query = "") =>
        await (await _client.SendAsync(HttpMethod.Get, $"/api/me/notifications{query}", token)).JsonAsync();

    private static string?[] Types(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString()).ToArray();

    [Fact]
    public async Task The_customer_is_told_a_process_was_opened_and_moved_but_the_analyst_who_acted_is_not()
    {
        var s = await ScenarioAsync();

        await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/advance", s.Staff, new { comment = "Seguindo" });

        var customer = await NotificationsAsync(s.Customer);
        customer.GetProperty("unreadCount").GetInt32().Should().Be(2);
        Types(customer).Should().Equal("StepAdvancedWithPendingDocuments", "Opened"); // newest first
        var latest = customer.GetProperty("items")[0];
        latest.GetProperty("processId").GetGuid().Should().Be(s.ProcessId);
        latest.GetProperty("processTitle").GetString().Should().Be("MTR — Jundiaí");
        latest.GetProperty("stepName").GetString().Should().Be("Coleta");
        latest.GetProperty("actorName").GetString().Should().Be("Jane Analyst");
        latest.GetProperty("readAt").ValueKind.Should().Be(JsonValueKind.Null);

        (await NotificationsAsync(s.Staff)).GetProperty("unreadCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task The_assignee_is_told_when_the_customer_sends_a_document()
    {
        var s = await ScenarioAsync();
        var procuracao = s.Process.GetProperty("documents")[0].GetProperty("id").GetGuid();

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 fictitious"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "procuracao.pdf");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/me/processes/{s.ProcessId}/documents/{procuracao}/versions") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Customer);
        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        var staff = await NotificationsAsync(s.Staff);
        Types(staff).Should().Equal("DocumentUploaded");
        staff.GetProperty("items")[0].GetProperty("details").GetString().Should().Be("Procuração");
        Types(await NotificationsAsync(s.Customer)).Should().NotContain("DocumentUploaded");
    }

    [Fact]
    public async Task A_new_assignee_is_told()
    {
        var s = await ScenarioAsync();
        var (otherId, otherToken) = await StaffAsync("Caio Admin");

        await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/assign", s.Staff, new { assigneeId = otherId });

        Types(await NotificationsAsync(otherToken)).Should().Equal("Assigned");
    }

    [Fact]
    public async Task Reading_is_recorded_once_and_the_staff_see_it_on_the_timeline()
    {
        var s = await ScenarioAsync();
        var id = (await NotificationsAsync(s.Customer)).GetProperty("items")[0].GetProperty("id").GetGuid();

        var read = await _client.SendAsync(HttpMethod.Post, $"/api/me/notifications/{id}/read", s.Customer, new { });
        read.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var firstRead = (await NotificationsAsync(s.Customer)).GetProperty("items")[0].GetProperty("readAt").GetDateTimeOffset();
        await _client.SendAsync(HttpMethod.Post, $"/api/me/notifications/{id}/read", s.Customer, new { });

        var after = await NotificationsAsync(s.Customer);
        after.GetProperty("unreadCount").GetInt32().Should().Be(0);
        after.GetProperty("items")[0].GetProperty("readAt").GetDateTimeOffset().Should().Be(firstRead);

        var process = await (await _client.SendAsync(HttpMethod.Get, $"/api/processes/{s.ProcessId}", s.Staff)).JsonAsync();
        process.GetProperty("events")[0].GetProperty("seenByCustomerAt").GetDateTimeOffset().Should().Be(firstRead);
    }

    [Fact]
    public async Task Read_all_and_unread_only()
    {
        var s = await ScenarioAsync();
        await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/comments", s.Staff, new { comment = "Recebemos" });

        (await NotificationsAsync(s.Customer, "?unreadOnly=true")).GetProperty("items").GetArrayLength().Should().Be(2);
        (await _client.SendAsync(HttpMethod.Post, "/api/me/notifications/read-all", s.Customer, new { })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await NotificationsAsync(s.Customer, "?unreadOnly=true")).GetProperty("items").GetArrayLength().Should().Be(0);
        (await NotificationsAsync(s.Customer)).GetProperty("items").GetArrayLength().Should().Be(2);
    }

    private static string?[] EventTypes(JsonElement process) =>
        process.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString()).ToArray();

    [Fact]
    public async Task The_customer_comments_on_their_process_and_the_assignee_is_told()
    {
        var s = await ScenarioAsync();

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/me/processes/{s.ProcessId}/comments", s.Customer, new { comment = "Enviamos amanhã" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var comment = (await response.JsonAsync()).GetProperty("events").EnumerateArray().Last();
        comment.GetProperty("type").GetString().Should().Be("Commented");
        comment.GetProperty("comment").GetString().Should().Be("Enviamos amanhã");
        comment.GetProperty("byCustomer").GetBoolean().Should().BeTrue();
        Types(await NotificationsAsync(s.Staff)).Should().Equal("Commented");
    }

    [Fact]
    public async Task A_customer_comment_needs_text_and_their_own_process()
    {
        var s = await ScenarioAsync();

        (await _client.SendAsync(HttpMethod.Post, $"/api/me/processes/{s.ProcessId}/comments", s.Customer, new { comment = " " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.SendAsync(HttpMethod.Post, $"/api/me/processes/{Guid.NewGuid()}/comments", s.Customer, new { comment = "Oi" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Internal_notes_stay_with_the_staff()
    {
        var s = await ScenarioAsync();
        var (_, colleague) = await StaffAsync("Caio Admin");

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/notes", colleague, new { comment = "Cliente costuma atrasar" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffView = await (await _client.SendAsync(HttpMethod.Get, $"/api/processes/{s.ProcessId}", s.Staff)).JsonAsync();
        EventTypes(staffView).Should().Contain("InternalNote");
        staffView.GetProperty("events").EnumerateArray().Last().GetProperty("byCustomer").GetBoolean().Should().BeFalse();
        Types(await NotificationsAsync(s.Staff)).Should().Equal("InternalNote"); // the assignee is told

        var customerView = await (await _client.SendAsync(HttpMethod.Get, $"/api/me/processes/{s.ProcessId}", s.Customer)).JsonAsync();
        EventTypes(customerView).Should().NotContain("InternalNote");
        Types(await NotificationsAsync(s.Customer)).Should().NotContain("InternalNote");
    }

    [Fact]
    public async Task The_customer_cannot_write_internal_notes()
    {
        var s = await ScenarioAsync();

        (await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/notes", s.Customer, new { comment = "Oi" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_staff_fix_the_title_and_the_customer_sees_it()
    {
        var s = await ScenarioAsync();

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/processes/{s.ProcessId}/title", s.Staff, new { title = "MTR — Jundiaí (filial)" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("title").GetString().Should().Be("MTR — Jundiaí (filial)");
        var customerView = await (await _client.SendAsync(HttpMethod.Get, $"/api/me/processes/{s.ProcessId}", s.Customer)).JsonAsync();
        customerView.GetProperty("title").GetString().Should().Be("MTR — Jundiaí (filial)");
        EventTypes(customerView).Should().Contain("Renamed");

        (await _client.SendAsync(HttpMethod.Put, $"/api/processes/{s.ProcessId}/title", s.Staff, new { title = " " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_late_customer_step_is_told_to_the_customer_and_the_assignee_once()
    {
        var s = await ScenarioAsync(collectDays: 5);
        using var scope = factory.CreateTenantScope();
        var overdue = scope.ServiceProvider.GetRequiredService<IOverdueStepService>();

        await overdue.NotifyOverdueAsync(DateTimeOffset.UtcNow.AddDays(6));
        await overdue.NotifyOverdueAsync(DateTimeOffset.UtcNow.AddDays(7));

        Types(await NotificationsAsync(s.Staff)).Should().Equal("StepOverdue");
        Types(await NotificationsAsync(s.Customer)).Count(t => t == "StepOverdue").Should().Be(1);
        var customerView = await (await _client.SendAsync(HttpMethod.Get, $"/api/me/processes/{s.ProcessId}", s.Customer)).JsonAsync();
        var late = customerView.GetProperty("events").EnumerateArray().Last();
        late.GetProperty("type").GetString().Should().Be("StepOverdue");
        late.GetProperty("actorName").GetString().Should().Be("Howly");
    }

    [Fact]
    public async Task Nobody_reads_someone_else_s_notification()
    {
        var s = await ScenarioAsync();
        var id = (await NotificationsAsync(s.Customer)).GetProperty("items")[0].GetProperty("id").GetGuid();

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/me/notifications/{id}/read", s.Staff, new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await NotificationsAsync(s.Customer)).GetProperty("unreadCount").GetInt32().Should().Be(1);
    }
}
