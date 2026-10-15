using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>
/// Every document upload (customer and staff, requested, extra and examples) takes PDF, DOCX, XLSX, JPG/JPEG, PNG or
/// HEIC/HEIF only, checked by extension and by content. Photos are kept as sent, metadata included (proof of the visit).
/// </summary>
public class AllowedUploadFormatsEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nfictitious test content\n%%EOF");

    /// <summary>A JPEG whose Exif segment carries GPS-like text; the bytes must come back the same.</summary>
    private static readonly byte[] JpegWithExif =
    [
        0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x20, .. "Exif\0\0"u8, .. "GPS -23.5505,-46.6333 visita"u8, 0xFF, 0xD9,
    ];

    private static readonly byte[] Heic = [0x00, 0x00, 0x00, 0x18, .. "ftypheic"u8, 0x00, 0x00, 0x00, 0x00, .. "mif1heic"u8];

    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"9{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private record Scenario(string StaffToken, string CustomerToken, Guid ProcessId, Guid DocumentId);

    /// <summary>A customer with portal access and a process whose first step (theirs) asks for one document.</summary>
    private async Task<Scenario> ScenarioAsync()
    {
        var username = NewUsername("formats");
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
            name = $"LO {Guid.NewGuid():N}"[..12],
            steps = new object[]
            {
                new
                {
                    name = "Coleta de documentos", owner = "Customer", durationDays = 5, isOptional = false,
                    documents = new[] { new { name = "Foto da área", isRequired = true } }, deliverables = Array.Empty<string>(),
                },
            },
        })).JsonAsync();

        var process = await (await _client.SendAsync(HttpMethod.Post, "/api/processes", staff,
            new { customerId = customer.GetProperty("id").GetGuid(), processTypeId = type.GetProperty("id").GetGuid(), title = "LO — Jundiaí" })).JsonAsync();
        var document = process.GetProperty("documents").EnumerateArray().Single(d => d.GetProperty("name").GetString() == "Foto da área");
        return new Scenario(staff, customerToken, process.GetProperty("id").GetGuid(), document.GetProperty("id").GetGuid());
    }

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

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.JsonAsync();
        problem.GetProperty("errors").GetProperty("file").EnumerateArray().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("observacoes.txt")]
    [InlineData("contrato.doc")]
    [InlineData("arquivos.zip")]
    public async Task The_customer_cannot_send_other_formats(string fileName)
    {
        var s = await ScenarioAsync();

        var response = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{s.DocumentId}/versions", Pdf, fileName);

        await ShouldBeRefusedAsync(response);
    }

    [Fact]
    public async Task A_file_renamed_to_an_allowed_extension_is_refused()
    {
        var s = await ScenarioAsync();

        var response = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{s.DocumentId}/versions",
            Encoding.ASCII.GetBytes("apenas texto, não é PDF"), "licenca.pdf");

        await ShouldBeRefusedAsync(response);
    }

    [Fact]
    public async Task The_staff_cannot_add_other_formats_either()
    {
        var s = await ScenarioAsync();

        var version = await UploadAsync(s.StaffToken, $"/api/processes/{s.ProcessId}/documents/{s.DocumentId}/versions", Pdf, "planilha.xls");
        var extra = await UploadAsync(s.StaffToken, $"/api/processes/{s.ProcessId}/documents", Pdf, "notas.txt", new() { ["name"] = "Notas" });
        var customerExtra = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents", Pdf, "notas.csv", new() { ["name"] = "Notas" });
        var example = await UploadAsync(s.StaffToken, "/api/process-types/examples", Pdf, "modelo.odt");

        await ShouldBeRefusedAsync(version);
        await ShouldBeRefusedAsync(extra);
        await ShouldBeRefusedAsync(customerExtra);
        await ShouldBeRefusedAsync(example);
    }

    [Fact]
    public async Task A_heic_photo_from_a_phone_is_accepted()
    {
        var s = await ScenarioAsync();

        var response = await UploadAsync(s.StaffToken, $"/api/processes/{s.ProcessId}/documents/{s.DocumentId}/versions", Heic, "IMG_0001.HEIC");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Photos_keep_their_metadata_as_sent()
    {
        var s = await ScenarioAsync();

        var uploaded = await UploadAsync(s.CustomerToken, $"/api/me/processes/{s.ProcessId}/documents/{s.DocumentId}/versions", JpegWithExif, "area.jpg");
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK);
        var versionId = (await uploaded.JsonAsync()).GetProperty("documents").EnumerateArray()
            .Single(d => d.GetProperty("id").GetGuid() == s.DocumentId).GetProperty("versions")[0].GetProperty("id").GetGuid();

        var download = await _client.SendAsync(HttpMethod.Get, $"/api/processes/{s.ProcessId}/documents/{s.DocumentId}/versions/{versionId}/file", s.StaffToken);

        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(JpegWithExif);
    }

    [Fact]
    public async Task Office_documents_and_pdfs_still_go_through()
    {
        var s = await ScenarioAsync();
        byte[] docx = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00, .. "[Content_Types].xml"u8];

        var pdf = await UploadAsync(s.StaffToken, $"/api/processes/{s.ProcessId}/documents", Pdf, "relatorio.pdf", new() { ["name"] = "Relatório" });
        var word = await UploadAsync(s.StaffToken, $"/api/processes/{s.ProcessId}/documents", docx, "contrato.docx", new() { ["name"] = "Contrato" });
        var example = await UploadAsync(s.StaffToken, "/api/process-types/examples", docx, "modelo.docx");

        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        word.StatusCode.Should().Be(HttpStatusCode.OK);
        example.StatusCode.Should().Be(HttpStatusCode.Created, await example.Content.ReadAsStringAsync());
    }
}
