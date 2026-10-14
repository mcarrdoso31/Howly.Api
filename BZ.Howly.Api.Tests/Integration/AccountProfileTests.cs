using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BZ.Howly.Api.Tests.Storage;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class AccountProfileTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static readonly object FullProfile = new
    {
        displayName = "Bia Analista",
        nickname = "Bia",
        email = "bia@howly.example",
        phone = "(11) 91234-5678",
        secondaryEmail = "bia.backup@howly.example",
        secondaryPhone = "(21) 3456-7890",
    };

    private async Task<(Guid Id, string Token)> StaffAsync(string role = "Analyst")
    {
        var username = NewUsername("profile");
        var id = await factory.CreateUserAsync(username, Password, "Jane Analyst", role);
        return (id, await _client.LoginInternalAsync(username));
    }

    private async Task<(Guid Id, string Token)> CustomerAsync()
    {
        var cnpj = $"4{Random.Shared.Next(1_000_000, 9_999_999)}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(cnpj, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        cnpj = $"{cnpj}{first}{Digit(cnpj + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
        var id = await factory.CreateUserAsync(cnpj, Password, "Customer Co", "Customer");
        var token = (await _client.LoginCustomerAsync(cnpj, Password)).GetProperty("accessToken").GetString()!;
        return (id, token);
    }

    private Task<HttpResponseMessage> UploadAvatarAsync(string token, byte[] content, string fileName = "photo.png")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/me/avatar") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static byte[] PngOf(int length) => [.. ImageFormatTests.Png, .. new byte[Math.Max(0, length - ImageFormatTests.Png.Length)]];

    // ---- profile ----

    [Fact]
    public async Task Get_returns_the_signed_in_account()
    {
        var (id, token) = await StaffAsync();

        var profile = await (await _client.SendAsync(HttpMethod.Get, "/api/me/profile", token)).JsonAsync();

        profile.GetProperty("id").GetGuid().Should().Be(id);
        profile.GetProperty("displayName").GetString().Should().Be("Jane Analyst");
        profile.GetProperty("avatarVersion").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Staff_update_their_own_profile_and_the_next_token_carries_it()
    {
        var (_, token) = await StaffAsync();

        var response = await _client.SendAsync(HttpMethod.Put, "/api/me/profile", token, FullProfile);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.JsonAsync();
        profile.GetProperty("nickname").GetString().Should().Be("Bia");
        profile.GetProperty("phone").GetString().Should().Be("11912345678");
        profile.GetProperty("secondaryEmail").GetString().Should().Be("bia.backup@howly.example");

        var me = await (await _client.SendAsync(HttpMethod.Get, "/api/auth/me", token)).JsonAsync();
        me.GetProperty("name").GetString().Should().Be("Bia Analista");
        me.GetProperty("nickname").GetString().Should().Be("Bia");
    }

    [Fact]
    public async Task Update_with_invalid_data_returns_400_pointing_to_the_field()
    {
        var (_, token) = await StaffAsync();

        var response = await _client.SendAsync(HttpMethod.Put, "/api/me/profile", token,
            new { displayName = "Bia", phone = "1234" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("phone", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Customers_edit_their_company_instead_of_the_staff_profile()
    {
        var (_, token) = await CustomerAsync();

        var response = await _client.SendAsync(HttpMethod.Put, "/api/me/profile", token, FullProfile);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- avatar ----

    [Fact]
    public async Task Upload_stores_the_avatar_and_anyone_signed_in_staff_can_see_it()
    {
        var (id, token) = await StaffAsync();
        var image = PngOf(200);

        var upload = await UploadAvatarAsync(token, image);

        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        (await upload.JsonAsync()).GetProperty("avatarVersion").GetString().Should().NotBeNullOrEmpty();

        var (_, otherToken) = await StaffAsync();
        var download = await _client.SendAsync(HttpMethod.Get, $"/api/users/{id}/avatar", otherToken);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        download.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(image);
    }

    [Fact]
    public async Task Upload_rejects_files_that_are_not_images_whatever_the_name()
    {
        var (_, token) = await StaffAsync();

        var response = await UploadAvatarAsync(token, Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>"), "photo.png");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("file", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_rejects_images_over_2_megabytes()
    {
        var (_, token) = await StaffAsync();

        var response = await UploadAvatarAsync(token, PngOf(2 * 1024 * 1024 + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Replacing_and_removing_the_avatar()
    {
        var (id, token) = await StaffAsync();
        await UploadAvatarAsync(token, PngOf(100));
        await UploadAvatarAsync(token, [.. ImageFormatTests.Jpeg, 1, 2, 3]);

        var current = await _client.SendAsync(HttpMethod.Get, $"/api/users/{id}/avatar", token);
        current.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");

        var removed = await _client.SendAsync(HttpMethod.Delete, "/api/me/avatar", token);

        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removed.JsonAsync()).GetProperty("avatarVersion").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await _client.SendAsync(HttpMethod.Get, $"/api/users/{id}/avatar", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customers_upload_their_logo_and_see_staff_photos()
    {
        var (customerId, customerToken) = await CustomerAsync();
        var (staffId, staffToken) = await StaffAsync();
        await UploadAvatarAsync(staffToken, PngOf(100));

        (await UploadAvatarAsync(customerToken, PngOf(100))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.SendAsync(HttpMethod.Get, $"/api/users/{customerId}/avatar", customerToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(HttpMethod.Get, $"/api/users/{staffId}/avatar", customerToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.SendAsync(HttpMethod.Get, $"/api/users/{customerId}/avatar", staffToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Customers_cannot_see_the_logo_of_another_customer()
    {
        var (otherId, otherToken) = await CustomerAsync();
        await UploadAvatarAsync(otherToken, PngOf(100));
        var (_, token) = await CustomerAsync();

        var response = await _client.SendAsync(HttpMethod.Get, $"/api/users/{otherId}/avatar", token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Avatar_needs_a_signed_in_user()
    {
        var (id, token) = await StaffAsync();
        await UploadAvatarAsync(token, PngOf(100));

        (await _client.SendAsync(HttpMethod.Get, $"/api/users/{id}/avatar", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
