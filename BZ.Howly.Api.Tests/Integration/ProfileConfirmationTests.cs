using System.Net;
using System.Text.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class ProfileConfirmationTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private const string NewPassword = "Brand-New-Pass-2";

    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"3{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var second = Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{second}";
    }

    private async Task<string> AnalystTokenAsync()
    {
        var username = NewUsername("analyst");
        await factory.CreateUserAsync(username, Password, "Jane Analyst", "Analyst");
        return await _client.LoginInternalAsync(username);
    }

    /// <summary>A customer created by an analyst who already replaced the temporary password.</summary>
    private async Task<(Guid Id, string Cnpj, JsonElement Login, string AnalystToken)> CustomerAfterPasswordChangeAsync()
    {
        var analystToken = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await (await _client.SendAsync(HttpMethod.Post, "/api/customers", analystToken,
            new { cnpj, companyName = "Beagle Industrial Ltda", contacts = new[] { Contact() } })).JsonAsync();
        var temporaryPassword = created.GetProperty("temporaryPassword").GetString()!;

        var pending = (await _client.LoginCustomerAsync(cnpj, temporaryPassword)).GetProperty("accessToken").GetString();
        var changed = await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", pending,
            new { currentPassword = temporaryPassword, newPassword = NewPassword });
        changed.EnsureSuccessStatusCode();

        return (created.GetProperty("id").GetGuid(), cnpj, await changed.JsonAsync(), analystToken);
    }

    private static object Contact(string name = "Bia Souza", string email = "bia@beagle.example", string phone = "(11) 91234-5678", bool receivesNotices = true) =>
        new { name, email, phone, receivesNotices };

    private static string TokenOf(JsonElement login) => login.GetProperty("accessToken").GetString()!;

    [Fact]
    public async Task New_customer_must_confirm_the_data_after_changing_the_password()
    {
        var (_, _, login, _) = await CustomerAfterPasswordChangeAsync();

        var user = login.GetProperty("user");
        user.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
        user.GetProperty("mustConfirmProfile").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Staff_users_never_need_to_confirm_a_profile()
    {
        var username = NewUsername("analyst");
        await factory.CreateUserAsync(username, Password, "Jane Analyst", "Analyst");

        var me = await (await _client.SendAsync(HttpMethod.Get, "/api/auth/me", await _client.LoginInternalAsync(username))).JsonAsync();

        me.GetProperty("mustConfirmProfile").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Pending_customer_reads_the_profile_to_review()
    {
        var (_, cnpj, login, _) = await CustomerAfterPasswordChangeAsync();

        var response = await _client.SendAsync(HttpMethod.Get, "/api/me/company", TokenOf(login));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.JsonAsync();
        profile.GetProperty("cnpj").GetString().Should().Be(cnpj);
        profile.GetProperty("companyName").GetString().Should().Be("Beagle Industrial Ltda");
        var contact = profile.GetProperty("contacts").EnumerateArray().Should().ContainSingle().Subject;
        contact.GetProperty("name").GetString().Should().Be("Bia Souza");
        contact.GetProperty("phone").GetString().Should().Be("11912345678");
        profile.GetProperty("confirmedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Confirm_saves_the_contact_and_the_next_session_is_released()
    {
        var (id, cnpj, login, analystToken) = await CustomerAfterPasswordChangeAsync();

        var response = await _client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", TokenOf(login),
            new { contacts = new[] { Contact(), Contact("Caio Lima", "caio@beagle.example", "(21) 3456-7890", false) } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("confirmedAt").ValueKind.Should().Be(JsonValueKind.String);

        var again = await _client.LoginCustomerAsync(cnpj, NewPassword);
        again.GetProperty("user").GetProperty("mustConfirmProfile").GetBoolean().Should().BeFalse();

        var seenByStaff = await (await _client.SendAsync(HttpMethod.Get, $"/api/customers/{id}", analystToken)).JsonAsync();
        seenByStaff.GetProperty("contacts").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Equal("Bia Souza", "Caio Lima");
        seenByStaff.GetProperty("confirmedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Confirm_with_invalid_contact_returns_400_pointing_to_the_field()
    {
        var (_, _, login, _) = await CustomerAfterPasswordChangeAsync();

        var response = await _client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", TokenOf(login),
            new { contacts = new[] { Contact(email: "nope") } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("contacts[0].email", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Profile_needs_the_password_changed_first()
    {
        var analystToken = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await (await _client.SendAsync(HttpMethod.Post, "/api/customers", analystToken,
            new { cnpj, companyName = "Beagle Industrial Ltda", contacts = new[] { Contact() } })).JsonAsync();
        var pending = (await _client.LoginCustomerAsync(cnpj, created.GetProperty("temporaryPassword").GetString()!)).GetProperty("accessToken").GetString();

        var response = await _client.SendAsync(HttpMethod.Get, "/api/me/company", pending);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.JsonAsync()).GetProperty("title").GetString().Should().Be("Password change required.");
    }

    [Fact]
    public async Task Staff_cannot_use_the_customer_company()
    {
        var response = await _client.SendAsync(HttpMethod.Get, "/api/me/company", await AnalystTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pending_customer_can_still_save_preferences()
    {
        var (_, _, login, _) = await CustomerAfterPasswordChangeAsync();

        var response = await _client.SendAsync(HttpMethod.Put, "/api/me/preferences", TokenOf(login),
            new { themeMode = "dark", themePreset = "howly", primaryColor = (string?)null, language = "pt-BR" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- later edits, from the customer profile ----

    [Fact]
    public async Task Customer_sets_a_trade_name_that_becomes_the_name_shown()
    {
        var (id, cnpj, login, analystToken) = await CustomerAfterPasswordChangeAsync();
        await _client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", TokenOf(login), new { contacts = new[] { Contact() } });
        var token = TokenOf(await _client.LoginCustomerAsync(cnpj, NewPassword));

        var response = await _client.SendAsync(HttpMethod.Put, "/api/me/company/trade-name", token, new { tradeName = "Beagle Tech" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("tradeName").GetString().Should().Be("Beagle Tech");
        (await _client.LoginCustomerAsync(cnpj, NewPassword)).GetProperty("user").GetProperty("name").GetString().Should().Be("Beagle Tech");
        var seenByStaff = await (await _client.SendAsync(HttpMethod.Get, $"/api/customers/{id}", analystToken)).JsonAsync();
        seenByStaff.GetProperty("companyName").GetString().Should().Be("Beagle Industrial Ltda");
        seenByStaff.GetProperty("tradeName").GetString().Should().Be("Beagle Tech");
    }

    [Fact]
    public async Task Customer_updates_the_contacts_without_losing_the_confirmation()
    {
        var (id, cnpj, login, analystToken) = await CustomerAfterPasswordChangeAsync();
        await _client.SendAsync(HttpMethod.Post, "/api/me/company/confirm", TokenOf(login), new { contacts = new[] { Contact() } });
        var token = TokenOf(await _client.LoginCustomerAsync(cnpj, NewPassword));

        var response = await _client.SendAsync(HttpMethod.Put, "/api/me/company/contacts", token,
            new { contacts = new[] { Contact(), Contact("Caio Lima", "caio@beagle.example", "(21) 3456-7890", false) } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var company = await response.JsonAsync();
        company.GetProperty("contacts").GetArrayLength().Should().Be(2);
        company.GetProperty("confirmedAt").ValueKind.Should().Be(JsonValueKind.String);
        var seenByStaff = await (await _client.SendAsync(HttpMethod.Get, $"/api/customers/{id}", analystToken)).JsonAsync();
        seenByStaff.GetProperty("contacts").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Contacts_update_validates_like_the_confirmation()
    {
        var (_, _, login, _) = await CustomerAfterPasswordChangeAsync();

        var response = await _client.SendAsync(HttpMethod.Put, "/api/me/company/contacts", TokenOf(login),
            new { contacts = new[] { Contact(phone: "123") } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("contacts[0].phone", out _).Should().BeTrue();
    }
}
