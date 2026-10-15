using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

public class CustomerEndpointsTests(HowlyApiFactory factory) : IClassFixture<HowlyApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { HandleCookies = false });

    // Fixture generator for fictitious CNPJs: unique base per call plus the standard check digits.
    // If it ever produced an invalid CNPJ, the API would answer 400 and the tests would fail loudly.
    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"2{Interlocked.Increment(ref _sequence):D7}0001";
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

    private static object Contact(string name = "Bia Souza", string email = "contato@beagle.example", string phone = "(11) 91234-5678", bool receivesNotices = true) =>
        new { name, email, phone, receivesNotices };

    private static object NewCustomerBody(string cnpj, string companyName = "Beagle Industrial Ltda", string contactEmail = "contato@beagle.example") =>
        new { document = cnpj, companyName, contacts = new[] { Contact(email: contactEmail) } };

    private async Task<JsonElement> CreateCustomerAsync(string token, string cnpj, string companyName = "Beagle Industrial Ltda")
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/customers", token, NewCustomerBody(cnpj, companyName));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.JsonAsync();
    }

    // ---- create ----

    [Fact]
    public async Task Create_as_analyst_returns_201_with_a_temporary_password()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var masked = $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

        var response = await _client.SendAsync(HttpMethod.Post, "/api/customers", token, NewCustomerBody(masked));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.JsonAsync();
        response.Headers.Location!.ToString().Should().EndWith($"/api/customers/{body.GetProperty("id").GetGuid()}");
        body.GetProperty("document").GetString().Should().Be(cnpj);
        body.GetProperty("companyName").GetString().Should().Be("Beagle Industrial Ltda");
        body.GetProperty("temporaryPassword").GetString().Should().HaveLength(12);
    }

    [Fact]
    public async Task New_customer_logs_in_with_the_temporary_password_and_is_told_to_change_it()
    {
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(await AnalystTokenAsync(), cnpj);

        var login = await _client.LoginCustomerAsync(cnpj, created.GetProperty("temporaryPassword").GetString()!);

        login.GetProperty("user").GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
        login.GetProperty("user").GetProperty("name").GetString().Should().Be("Beagle Industrial Ltda");
    }

    [Fact]
    public async Task Create_with_existing_cnpj_returns_409()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        await CreateCustomerAsync(token, cnpj);

        var response = await _client.SendAsync(HttpMethod.Post, "/api/customers", token, NewCustomerBody(cnpj, "Another Name"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_with_invalid_cnpj_returns_400_pointing_to_the_field()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/customers", await AnalystTokenAsync(), NewCustomerBody("11222333000182"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("document", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_with_invalid_email_returns_400_and_creates_no_login()
    {
        var cnpj = NewCnpj();

        var response = await _client.SendAsync(HttpMethod.Post, "/api/customers", await AnalystTokenAsync(),
            NewCustomerBody(cnpj, contactEmail: "not-an-email"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("contacts[0].email", out _).Should().BeTrue();
        var login = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = cnpj, password = "anything-at-all" });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_as_customer_returns_403()
    {
        var cnpj = NewCnpj();
        await factory.CreateUserAsync(cnpj, Password, "Customer Co", "Customer");
        var token = (await _client.LoginCustomerAsync(cnpj, Password)).GetProperty("accessToken").GetString();

        var response = await _client.SendAsync(HttpMethod.Post, "/api/customers", token, NewCustomerBody(NewCnpj()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_without_token_returns_401()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/customers", null, NewCustomerBody(NewCnpj()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- read ----

    [Fact]
    public async Task Get_returns_details_with_the_pending_flag_but_never_the_password()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(token, cnpj);

        var response = await _client.SendAsync(HttpMethod.Get, $"/api/customers/{created.GetProperty("id").GetGuid()}", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("document").GetString().Should().Be(cnpj);
        var contact = body.GetProperty("contacts").EnumerateArray().Should().ContainSingle().Subject;
        contact.GetProperty("name").GetString().Should().Be("Bia Souza");
        contact.GetProperty("email").GetString().Should().Be("contato@beagle.example");
        contact.GetProperty("phone").GetString().Should().Be("11912345678");
        contact.GetProperty("receivesNotices").GetBoolean().Should().BeTrue();
        body.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
        body.TryGetProperty("temporaryPassword", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Get_unknown_customer_returns_404()
    {
        var response = await _client.SendAsync(HttpMethod.Get, $"/api/customers/{Guid.NewGuid()}", await AnalystTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_shows_flag_cleared_after_the_customer_changes_the_password()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(token, cnpj);
        var customerToken = (await _client.LoginCustomerAsync(cnpj, created.GetProperty("temporaryPassword").GetString()!))
            .GetProperty("accessToken").GetString();
        await _client.SendAsync(HttpMethod.Post, "/api/auth/change-password", customerToken,
            new { currentPassword = created.GetProperty("temporaryPassword").GetString(), newPassword = "Brand-New-Pass-2" });

        var body = await (await _client.SendAsync(HttpMethod.Get, $"/api/customers/{created.GetProperty("id").GetGuid()}", token)).JsonAsync();

        body.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task List_filters_by_company_name_or_cnpj_and_shows_the_pending_flag()
    {
        var token = await AnalystTokenAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var cnpj = NewCnpj();
        await CreateCustomerAsync(token, cnpj, $"Howl {marker} Ltda");
        await CreateCustomerAsync(token, NewCnpj(), "Unrelated Ltda");

        var byName = await (await _client.SendAsync(HttpMethod.Get, $"/api/customers?search={marker.ToUpperInvariant()}", token)).JsonAsync();
        var byCnpj = await (await _client.SendAsync(HttpMethod.Get, $"/api/customers?search={cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}", token)).JsonAsync();

        var item = byName.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("document").GetString().Should().Be(cnpj);
        item.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
        byName.GetProperty("totalCount").GetInt32().Should().Be(1);
        byCnpj.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("document").GetString()).Should().Contain(cnpj);
    }

    [Fact]
    public async Task List_paginates()
    {
        var token = await AnalystTokenAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
            await CreateCustomerAsync(token, NewCnpj(), $"Page {marker} {i}");

        var page2 = await (await _client.SendAsync(HttpMethod.Get, $"/api/customers?search={marker}&page=2&pageSize=2", token)).JsonAsync();

        page2.GetProperty("items").GetArrayLength().Should().Be(1);
        page2.GetProperty("page").GetInt32().Should().Be(2);
        page2.GetProperty("pageSize").GetInt32().Should().Be(2);
        page2.GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    // ---- reset password ----

    [Fact]
    public async Task ResetPassword_issues_a_new_temporary_password_and_flags_the_customer_again()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(token, cnpj);
        var id = created.GetProperty("id").GetGuid();

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/customers/{id}/reset-password", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var newPassword = (await response.JsonAsync()).GetProperty("temporaryPassword").GetString()!;
        newPassword.Should().HaveLength(12).And.NotBe(created.GetProperty("temporaryPassword").GetString());

        var oldLogin = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = cnpj, password = created.GetProperty("temporaryPassword").GetString() });
        oldLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var newLogin = await _client.LoginCustomerAsync(cnpj, newPassword);
        newLogin.GetProperty("user").GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_ends_the_customer_sessions()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(token, cnpj);
        var login = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = cnpj, password = created.GetProperty("temporaryPassword").GetString() });
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("howly_refresh=")).Split(';')[0];

        await _client.SendAsync(HttpMethod.Post, $"/api/customers/{created.GetProperty("id").GetGuid()}/reset-password", token);

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refresh.Headers.Add("Cookie", cookie);
        (await _client.SendAsync(refresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResetPassword_for_unknown_customer_returns_404()
    {
        var response = await _client.SendAsync(HttpMethod.Post, $"/api/customers/{Guid.NewGuid()}/reset-password", await AnalystTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- status in responses ----

    [Fact]
    public async Task Get_shows_a_new_customer_as_active_and_not_confirmed()
    {
        var token = await AnalystTokenAsync();
        var created = await CreateCustomerAsync(token, NewCnpj());

        var body = await (await _client.SendAsync(HttpMethod.Get, $"/api/customers/{created.GetProperty("id").GetGuid()}", token)).JsonAsync();

        body.GetProperty("isActive").GetBoolean().Should().BeTrue();
        body.GetProperty("confirmedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---- update ----

    [Fact]
    public async Task Update_changes_the_data_and_the_name_shown_at_login()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(token, cnpj);
        var id = created.GetProperty("id").GetGuid();

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/customers/{id}", token,
            new { companyName = "Beagle Industrial S.A.", contacts = new[] { Contact(), Contact("Caio Lima", "caio@beagle.example", "(21) 3456-7890", false) } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("companyName").GetString().Should().Be("Beagle Industrial S.A.");
        body.GetProperty("contacts").EnumerateArray().Select(c => c.GetProperty("name").GetString()).Should().Equal("Bia Souza", "Caio Lima");
        body.GetProperty("document").GetString().Should().Be(cnpj);
        var login = await _client.LoginCustomerAsync(cnpj, created.GetProperty("temporaryPassword").GetString()!);
        login.GetProperty("user").GetProperty("name").GetString().Should().Be("Beagle Industrial S.A.");
    }

    [Fact]
    public async Task Update_with_invalid_data_returns_400_pointing_to_the_field()
    {
        var token = await AnalystTokenAsync();
        var created = await CreateCustomerAsync(token, NewCnpj());

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/customers/{created.GetProperty("id").GetGuid()}", token,
            new { companyName = "Beagle", contacts = new[] { Contact(), Contact("Caio", "caio@beagle.example", "123") } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("contacts[1].phone", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Update_unknown_customer_returns_404()
    {
        var response = await _client.SendAsync(HttpMethod.Put, $"/api/customers/{Guid.NewGuid()}", await AnalystTokenAsync(),
            new { companyName = "Beagle", contacts = new[] { Contact() } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- deactivate / reactivate ----

    [Fact]
    public async Task Deactivate_blocks_login_with_403_account_inactive()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(token, cnpj);
        var password = created.GetProperty("temporaryPassword").GetString();

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/customers/{created.GetProperty("id").GetGuid()}/deactivate", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("isActive").GetBoolean().Should().BeFalse();
        var login = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = cnpj, password });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await login.JsonAsync()).GetProperty("code").GetString().Should().Be("account_inactive");
    }

    [Fact]
    public async Task Inactive_customer_with_wrong_password_still_gets_the_generic_401()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(token, cnpj);
        await _client.SendAsync(HttpMethod.Post, $"/api/customers/{created.GetProperty("id").GetGuid()}/deactivate", token);

        var login = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = cnpj, password = "Wrong-Password-1" });

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivate_ends_the_customer_sessions()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(token, cnpj);
        var login = await _client.PostAsJsonAsync("/api/auth/customer/login", new { document = cnpj, password = created.GetProperty("temporaryPassword").GetString() });
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("howly_refresh=")).Split(';')[0];

        await _client.SendAsync(HttpMethod.Post, $"/api/customers/{created.GetProperty("id").GetGuid()}/deactivate", token);

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refresh.Headers.Add("Cookie", cookie);
        (await _client.SendAsync(refresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reactivate_allows_login_again()
    {
        var token = await AnalystTokenAsync();
        var cnpj = NewCnpj();
        var created = await CreateCustomerAsync(token, cnpj);
        var id = created.GetProperty("id").GetGuid();
        await _client.SendAsync(HttpMethod.Post, $"/api/customers/{id}/deactivate", token);

        var response = await _client.SendAsync(HttpMethod.Post, $"/api/customers/{id}/reactivate", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("isActive").GetBoolean().Should().BeTrue();
        await _client.LoginCustomerAsync(cnpj, created.GetProperty("temporaryPassword").GetString()!);
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("reactivate")]
    public async Task Status_change_of_unknown_customer_returns_404(string action)
    {
        var response = await _client.SendAsync(HttpMethod.Post, $"/api/customers/{Guid.NewGuid()}/{action}", await AnalystTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_shows_active_customers_by_default_and_filters_by_status()
    {
        var token = await AnalystTokenAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateCustomerAsync(token, NewCnpj(), $"Active {marker}");
        var inactive = await CreateCustomerAsync(token, NewCnpj(), $"Inactive {marker}");
        await _client.SendAsync(HttpMethod.Post, $"/api/customers/{inactive.GetProperty("id").GetGuid()}/deactivate", token);

        async Task<string?[]> NamesAsync(string query) =>
            (await (await _client.SendAsync(HttpMethod.Get, $"/api/customers?search={marker}{query}", token)).JsonAsync())
                .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("companyName").GetString()).ToArray();

        (await NamesAsync("")).Should().Equal($"Active {marker}");
        (await NamesAsync("&status=inactive")).Should().Equal($"Inactive {marker}");
        (await NamesAsync("&status=all")).Should().Equal($"Active {marker}", $"Inactive {marker}");
    }

    [Fact]
    public async Task List_items_carry_the_status_fields()
    {
        var token = await AnalystTokenAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateCustomerAsync(token, NewCnpj(), $"Status {marker}");

        var item = (await (await _client.SendAsync(HttpMethod.Get, $"/api/customers?search={marker}", token)).JsonAsync())
            .GetProperty("items")[0];

        item.GetProperty("isActive").GetBoolean().Should().BeTrue();
        item.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
        item.GetProperty("confirmedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
