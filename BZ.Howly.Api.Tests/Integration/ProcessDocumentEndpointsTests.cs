using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class ProcessDocumentEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nfictitious test content\n%%EOF");

    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"6{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Scenario(string StaffToken, string CustomerToken, Guid ProcessId, JsonElement Process);

    /// <summary>A customer with portal access and an MTR-like process whose first step asks for two documents.</summary>
    private async Task<Scenario> ScenarioAsync()
    {
        var username = NewUsername("docs");
        await factory.CreateUserAsync(username, Password, "Jane Analyst", "Analyst");
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
                new
                {
                    name = "Coleta de documentos", owner = "Customer", durationDays = 5, isOptional = false,
                    documents = new[] { new { name = "Licença de operação de máquinas agrícolas", isRequired = true }, new { name = "CNH do operador", isRequired = false } },
                    deliverables = Array.Empty<string>(),
                },
                new { name = "Emissão", owner = "Agency", agencyName = "IBAMA", isOptional = false, documents = Array.Empty<object>(), deliverables = new[] { "Licença" } },
            },
        })).JsonAsync();

        var process = await (await _client.SendAsync(HttpMethod.Post, "/api/processes", staff,
            new { customerId = customer.GetProperty("id").GetGuid(), processTypeId = type.GetProperty("id").GetGuid(), title = "MTR — Jundiaí" })).JsonAsync();
        return new Scenario(staff, customerToken, process.GetProperty("id").GetGuid(), process);
    }

    private static JsonElement Document(JsonElement process, string name) =>
        process.GetProperty("documents").EnumerateArray().Single(d => d.GetProperty("name").GetString() == name);

    private Task<HttpResponseMessage> UploadAsync(string token, string url, byte[] content, string fileName, Dictionary<string, string>? fields = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        foreach (var (key, value) in fields ?? [])
            form.Add(new StringContent(value), key);

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task Opening_a_process_creates_a_field_per_requested_document_and_deliverable()
    {
        var s = await ScenarioAsync();

        var documents = s.Process.GetProperty("documents").EnumerateArray().ToList();
        documents.Select(d => (d.GetProperty("name").GetString(), d.GetProperty("kind").GetString(), d.GetProperty("status").GetString()))
            .Should().BeEquivalentTo(new[]
            {
                ("Licença de operação de máquinas agrícolas", "Requested", "AwaitingUpload"),
                ("CNH do operador", "Requested", "AwaitingUpload"),
                ("Licença", "Deliverable", "AwaitingUpload"),
            });
        Document(s.Process, "Licença de operação de máquinas agrícolas").GetProperty("isRequired").GetBoolean().Should().BeTrue();
        Document(s.Process, "Licença de operação de máquinas agrícolas").GetProperty("stepId").GetGuid()
            .Should().Be(s.Process.GetProperty("steps")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Customer_uploads_and_the_analyst_approves_or_rejects()
    {
        var s = await ScenarioAsync();
        var license = Document(s.Process, "Licença de operação de máquinas agrícolas").GetProperty("id").GetGuid();

        var uploaded = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{license}/versions", Pdf, "licenca.pdf");
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterUpload = Document(await uploaded.JsonAsync(), "Licença de operação de máquinas agrícolas");
        afterUpload.GetProperty("status").GetString().Should().Be("InReview");
        afterUpload.GetProperty("versions")[0].GetProperty("fileName").GetString().Should().Be("licenca.pdf");

        var rejected = await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/documents/{license}/reject", s.StaffToken, new { reason = "Documento vencido" });
        var afterReject = await rejected.JsonAsync();
        Document(afterReject, "Licença de operação de máquinas agrícolas").GetProperty("status").GetString().Should().Be("Rejected");
        afterReject.GetProperty("events").EnumerateArray().Last().GetProperty("type").GetString().Should().Be("DocumentRejected");

        await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{license}/versions", Pdf, "licenca-2026.pdf");
        var approved = await (await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/documents/{license}/approve", s.StaffToken, new { })).JsonAsync();
        var document = Document(approved, "Licença de operação de máquinas agrícolas");
        document.GetProperty("status").GetString().Should().Be("Approved");
        document.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("status").GetString()).Should().Equal("Rejected", "Approved");
    }

    [Fact]
    public async Task Review_needs_a_document_in_review_and_a_reason_to_reject()
    {
        var s = await ScenarioAsync();
        var license = Document(s.Process, "Licença de operação de máquinas agrícolas").GetProperty("id").GetGuid();

        (await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/documents/{license}/approve", s.StaffToken, new { }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{license}/versions", Pdf, "licenca.pdf");
        (await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/documents/{license}/reject", s.StaffToken, new { reason = " " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Advancing_with_pending_required_documents_is_recorded_with_their_names()
    {
        var s = await ScenarioAsync();

        var advanced = await (await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/advance", s.StaffToken, new { comment = "Cliente envia depois" })).JsonAsync();

        var advancedEvent = advanced.GetProperty("events").EnumerateArray().Single(e => e.GetProperty("type").GetString() == "StepAdvancedWithPendingDocuments");
        advancedEvent.GetProperty("details").GetString().Should().Be("Licença de operação de máquinas agrícolas");
        advancedEvent.GetProperty("actorName").GetString().Should().Be("Jane Analyst");
    }

    [Fact]
    public async Task Advancing_after_every_required_document_is_approved_is_a_normal_advance()
    {
        var s = await ScenarioAsync();
        var license = Document(s.Process, "Licença de operação de máquinas agrícolas").GetProperty("id").GetGuid();
        await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{license}/versions", Pdf, "licenca.pdf");
        await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/documents/{license}/approve", s.StaffToken, new { });

        var advanced = await (await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/advance", s.StaffToken, new { })).JsonAsync();

        advanced.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("type").GetString())
            .Should().Contain("StepAdvanced").And.NotContain("StepAdvancedWithPendingDocuments");
    }

    [Fact]
    public async Task Staff_publish_deliverables_and_the_customer_downloads_them()
    {
        var s = await ScenarioAsync();
        var deliverable = Document(s.Process, "Licença").GetProperty("id").GetGuid();

        var published = await (await UploadAsync(s.StaffToken, $"/api/processes/{s.ProcessId}/documents/{deliverable}/versions", Pdf, "LO-2026.pdf")).JsonAsync();
        var document = Document(published, "Licença");
        document.GetProperty("status").GetString().Should().Be("Published");
        published.GetProperty("events").EnumerateArray().Last().GetProperty("type").GetString().Should().Be("DeliverablePublished");

        var version = document.GetProperty("versions")[0].GetProperty("id").GetGuid();
        var download = await _client.SendAsync(HttpMethod.Get, $"/api/me/processes/{s.ProcessId}/documents/{deliverable}/versions/{version}/file", s.CustomerToken);

        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Content.Headers.ContentDisposition.FileNameStar.Should().Be("LO-2026.pdf");
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        download.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
    }

    [Fact]
    public async Task Customers_cannot_publish_deliverables_nor_review()
    {
        var s = await ScenarioAsync();
        var deliverable = Document(s.Process, "Licença").GetProperty("id").GetGuid();

        (await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{deliverable}/versions", Pdf, "x.pdf"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _client.SendAsync(HttpMethod.Post, $"/api/processes/{s.ProcessId}/documents/{deliverable}/approve", s.CustomerToken, new { }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Both_sides_add_other_documents_with_a_description()
    {
        var s = await ScenarioAsync();

        var fromCustomer = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents", Pdf, "mapa.pdf",
            new() { ["name"] = "Mapa da unidade" });
        var fromStaff = await UploadAsync(s.StaffToken, $"/api/processes/{s.ProcessId}/documents", Pdf, "oficio.pdf",
            new() { ["name"] = "Ofício do órgão", ["kind"] = "Deliverable" });

        fromCustomer.StatusCode.Should().Be(HttpStatusCode.OK);
        var extra = Document(await fromCustomer.JsonAsync(), "Mapa da unidade");
        extra.GetProperty("kind").GetString().Should().Be("Extra");
        extra.GetProperty("status").GetString().Should().Be("InReview");
        Document(await fromStaff.JsonAsync(), "Ofício do órgão").GetProperty("status").GetString().Should().Be("Published");

        var noName = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents", Pdf, "x.pdf");
        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("pagina.html")]
    [InlineData("desenho.svg")]
    public async Task Executables_scripts_and_web_pages_are_refused(string fileName)
    {
        var s = await ScenarioAsync();
        var license = Document(s.Process, "Licença de operação de máquinas agrícolas").GetProperty("id").GetGuid();

        var response = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{license}/versions", Pdf, fileName);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("file", out _).Should().BeTrue();
    }

    [Fact]
    public async Task An_executable_renamed_as_pdf_is_refused()
    {
        var s = await ScenarioAsync();
        var license = Document(s.Process, "Licença de operação de máquinas agrícolas").GetProperty("id").GetGuid();

        var response = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{license}/versions", [0x4D, 0x5A, 0x90, 0x00, 1, 2, 3], "licenca.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Office_files_are_accepted()
    {
        var s = await ScenarioAsync();
        var cnh = Document(s.Process, "CNH do operador").GetProperty("id").GetGuid();

        var response = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{cnh}/versions", [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4], "planilha.xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Other_customers_cannot_reach_the_documents()
    {
        var s = await ScenarioAsync();
        var other = await ScenarioAsync();
        var license = Document(s.Process, "Licença de operação de máquinas agrícolas").GetProperty("id").GetGuid();

        (await UploadAsync(other.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{license}/versions", Pdf, "x.pdf"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customer_lists_show_how_many_documents_wait_for_them()
    {
        var s = await ScenarioAsync();

        var item = (await (await _client.SendAsync(HttpMethod.Get, "/api/me/processes", s.CustomerToken)).JsonAsync()).GetProperty("items")[0];

        item.GetProperty("documentsToSend").GetInt32().Should().Be(2);
    }
}
