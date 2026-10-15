using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>An example file the staff attach to a requested document, for the customer to download and follow.</summary>
public class DocumentExampleEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";
    private const string Letter = "Carta de anuência";
    private static readonly byte[] Docx = [0x50, 0x4B, 0x03, 0x04, .. Encoding.ASCII.GetBytes("fictitious docx template")];

    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"7{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private async Task<string> StaffAsync(HttpClient? client = null, string? slug = null)
    {
        var username = NewUsername("examples");
        if (slug is null)
            await factory.CreateUserAsync(username, Password, "Jane Analyst", "Analyst");
        else
            await factory.CreateUserInAsync(slug, username, Password, "Jane Analyst", "Analyst");
        return await (client ?? _client).LoginInternalAsync(username);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string? token, string url, byte[] content, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string token, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static object TypeBody(Guid? exampleId, string? exampleFileName = null) => new
    {
        name = $"LP {Guid.NewGuid():N}"[..12],
        steps = new object[]
        {
            new
            {
                name = "Documentos", owner = "Customer", durationDays = 5,
                documents = new object[] { new { name = Letter, isRequired = true, exampleId, exampleFileName }, new { name = "CNH", isRequired = false } },
            },
        },
    };

    private static JsonElement Document(JsonElement process, string name) =>
        process.GetProperty("documents").EnumerateArray().Single(d => d.GetProperty("name").GetString() == name);

    private async Task<Guid> UploadExampleAsync(string staff, HttpClient? client = null)
    {
        var uploaded = await UploadAsync(client ?? _client, staff, "/api/process-types/examples", Docx, "modelo-carta.docx");
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);
        var example = await uploaded.JsonAsync();
        example.GetProperty("fileName").GetString().Should().Be("modelo-carta.docx");
        example.GetProperty("size").GetInt64().Should().Be(Docx.Length);
        return example.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_customer_downloads_the_example_of_a_requested_document()
    {
        var staff = await StaffAsync();
        var exampleId = await UploadExampleAsync(staff);

        // The file name comes from the upload, whatever the body says.
        var type = await (await _client.SendAsync(HttpMethod.Post, "/api/process-types", staff, TypeBody(exampleId, "renamed.exe"))).JsonAsync();
        var requirement = type.GetProperty("steps")[0].GetProperty("documents")[0];
        requirement.GetProperty("exampleId").GetGuid().Should().Be(exampleId);
        requirement.GetProperty("exampleFileName").GetString().Should().Be("modelo-carta.docx");

        var contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } };
        var cnpj = NewCnpj();
        var customer = await (await _client.SendAsync(HttpMethod.Post, "/api/customers", staff, new { cnpj, companyName = "Beagle Industrial Ltda", contacts })).JsonAsync();
        var temporary = customer.GetProperty("temporaryPassword").GetString()!;
        var pending = (await _client.LoginCustomerAsync(cnpj, temporary)).GetProperty("accessToken").GetString();
        var changed = await (await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", pending, new { currentPassword = temporary, newPassword = NewPassword })).JsonAsync();
        await _client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", changed.GetProperty("accessToken").GetString(), new { contacts });
        var customerToken = (await _client.LoginCustomerAsync(cnpj, NewPassword)).GetProperty("accessToken").GetString()!;

        var process = await (await _client.SendAsync(HttpMethod.Post, "/api/processes", staff,
            new { customerId = customer.GetProperty("id").GetGuid(), processTypeId = type.GetProperty("id").GetGuid(), title = "LP — Jundiaí" })).JsonAsync();
        var processId = process.GetProperty("id").GetGuid();
        var letter = Document(process, Letter);
        letter.GetProperty("exampleFileName").GetString().Should().Be("modelo-carta.docx");
        Document(process, "CNH").GetProperty("exampleFileName").ValueKind.Should().Be(JsonValueKind.Null);
        var letterId = letter.GetProperty("id").GetGuid();

        var byCustomer = await GetAsync(_client, customerToken, $"/api/me/processes/{processId}/documents/{letterId}/example");
        byCustomer.StatusCode.Should().Be(HttpStatusCode.OK);
        (await byCustomer.Content.ReadAsByteArrayAsync()).Should().Equal(Docx);
        byCustomer.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("modelo-carta.docx");

        var byStaff = await GetAsync(_client, staff, $"/api/processes/{processId}/documents/{letterId}/example");
        (await byStaff.Content.ReadAsByteArrayAsync()).Should().Equal(Docx);

        var fromType = await GetAsync(_client, staff, $"/api/process-types/examples/{exampleId}");
        (await fromType.Content.ReadAsByteArrayAsync()).Should().Equal(Docx);

        var cnh = Document(process, "CNH").GetProperty("id").GetGuid();
        (await GetAsync(_client, customerToken, $"/api/me/processes/{processId}/documents/{cnh}/example")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Editing_the_type_keeps_the_example()
    {
        var staff = await StaffAsync();
        var exampleId = await UploadExampleAsync(staff);
        var type = await (await _client.SendAsync(HttpMethod.Post, "/api/process-types", staff, TypeBody(exampleId))).JsonAsync();

        var updated = await (await _client.SendAsync(HttpMethod.Put, $"/api/process-types/{type.GetProperty("id").GetGuid()}", staff, TypeBody(exampleId))).JsonAsync();

        updated.GetProperty("steps")[0].GetProperty("documents")[0].GetProperty("exampleFileName").GetString().Should().Be("modelo-carta.docx");
    }

    [Fact]
    public async Task An_unknown_example_is_refused()
    {
        var staff = await StaffAsync();

        var response = await _client.SendAsync(HttpMethod.Post, "/api/process-types", staff, TypeBody(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("steps[0].documents[0].exampleId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Another_consultancy_cannot_use_nor_read_the_example()
    {
        var staff = await StaffAsync();
        var exampleId = await UploadExampleAsync(staff);
        var slug = $"ex-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Outra");
        var other = factory.CreatePortalClient(slug);
        var otherStaff = await StaffAsync(other, slug);

        (await other.SendAsync(HttpMethod.Post, "/api/process-types", otherStaff, TypeBody(exampleId))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAsync(other, otherStaff, $"/api/process-types/examples/{exampleId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Examples_go_through_the_upload_checks()
    {
        var staff = await StaffAsync();

        var response = await UploadAsync(_client, staff, "/api/process-types/examples", [0x4D, 0x5A, 0x90, 0x00], "modelo.exe");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("file", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Only_the_staff_upload_examples()
    {
        (await UploadAsync(_client, null, "/api/process-types/examples", Docx, "modelo.docx")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
