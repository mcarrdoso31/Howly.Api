using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Services.Identity;
using BZ.Howly.Api.Tests.Fakes;
using BZ.Howly.Api.Utils.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BZ.Howly.Api.Tests.Identity;

public class AuthServiceTests
{
    private const string CustomerCnpj = "11222333000181";
    private const string Password = "Correct-Horse-1";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeUserAccountStore _users = new();
    private readonly InMemoryRefreshTokenRepository _refreshTokens = new();
    private readonly JwtTokenService _tokenService;
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _tokenService = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "howly-api",
            Audience = "howly-web",
            SigningKey = "test-signing-key-with-at-least-32-bytes!",
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
            RefreshTokenLifetime = TimeSpan.FromDays(7),
        }), _time);
        _service = new AuthService(_users, _refreshTokens, _tokenService, _time);
    }

    // ---- customer login ----

    [Fact]
    public async Task LoginCustomer_with_valid_credentials_returns_session_with_customer_role()
    {
        var customer = _users.Add(CustomerCnpj, Password, "Customer");

        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;

        session.Should().NotBeNull();
        session!.Response.AccessToken.Should().NotBeNullOrEmpty();
        session.Response.ExpiresAt.Should().Be(new DateTimeOffset(2026, 10, 13, 12, 15, 0, TimeSpan.Zero));
        session.Response.User.Id.Should().Be(customer.Id);
        session.Response.User.Roles.Should().Equal("Customer");
    }

    [Fact]
    public async Task LoginCustomer_persists_only_the_hash_of_the_refresh_token()
    {
        var customer = _users.Add(CustomerCnpj, Password, "Customer");

        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;

        var stored = _refreshTokens.Tokens.Should().ContainSingle().Subject;
        stored.UserId.Should().Be(customer.Id);
        stored.TokenHash.Should().Be(_tokenService.HashRefreshToken(session!.RefreshToken));
        stored.TokenHash.Should().NotBe(session.RefreshToken);
        session.RefreshTokenExpiresAt.Should().Be(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task LoginCustomer_accepts_masked_cnpj()
    {
        _users.Add(CustomerCnpj, Password, "Customer");

        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest("11.222.333/0001-81", Password))).Session;

        session.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginCustomer_with_wrong_password_returns_null()
    {
        _users.Add(CustomerCnpj, Password, "Customer");

        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, "wrong"))).Session;

        session.Should().BeNull();
        _refreshTokens.Tokens.Should().BeEmpty();
    }

    [Fact]
    public async Task LoginCustomer_with_unknown_cnpj_returns_null()
    {
        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;

        session.Should().BeNull();
    }

    [Fact]
    public async Task LoginCustomer_with_invalid_cnpj_returns_null()
    {
        // username stored exactly as typed, so only CNPJ validation can reject it
        _users.Add("11222333000182", Password, "Customer");

        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest("11222333000182", Password))).Session;

        session.Should().BeNull();
    }

    [Fact]
    public async Task LoginCustomer_rejects_user_without_customer_role()
    {
        _users.Add(CustomerCnpj, Password, "Analyst");

        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;

        session.Should().BeNull();
    }

    [Fact]
    public async Task LoginCustomer_when_locked_out_returns_null_even_with_correct_password()
    {
        var customer = _users.Add(CustomerCnpj, Password, "Customer");
        _users.LockOut(customer.Id);

        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;

        session.Should().BeNull();
    }

    // ---- internal login ----

    [Theory]
    [InlineData("Analyst")]
    [InlineData("Admin")]
    public async Task LoginInternal_with_valid_credentials_returns_session(string role)
    {
        _users.Add("jane.doe", Password, role);

        var session = (await _service.LoginInternalAsync(new InternalLoginRequest("jane.doe", Password))).Session;

        session.Should().NotBeNull();
        session!.Response.User.Roles.Should().Equal(role);
    }

    [Fact]
    public async Task LoginInternal_trims_the_username()
    {
        _users.Add("jane.doe", Password, "Analyst");

        var session = (await _service.LoginInternalAsync(new InternalLoginRequest("  jane.doe ", Password))).Session;

        session.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginInternal_rejects_customer_even_with_correct_password()
    {
        _users.Add(CustomerCnpj, Password, "Customer");

        var session = (await _service.LoginInternalAsync(new InternalLoginRequest(CustomerCnpj, Password))).Session;

        session.Should().BeNull();
    }

    [Fact]
    public async Task LoginInternal_with_wrong_password_returns_null()
    {
        _users.Add("jane.doe", Password, "Analyst");

        var session = (await _service.LoginInternalAsync(new InternalLoginRequest("jane.doe", "wrong"))).Session;

        session.Should().BeNull();
    }

    // ---- refresh ----

    [Fact]
    public async Task Refresh_with_active_token_rotates_it()
    {
        _users.Add("jane.doe", Password, "Analyst");
        var login = (await _service.LoginInternalAsync(new InternalLoginRequest("jane.doe", Password))).Session;
        _time.Advance(TimeSpan.FromMinutes(20));

        var refreshed = await _service.RefreshAsync(login!.RefreshToken);

        refreshed.Should().NotBeNull();
        refreshed!.RefreshToken.Should().NotBe(login.RefreshToken);
        refreshed.Response.ExpiresAt.Should().Be(new DateTimeOffset(2026, 10, 13, 12, 35, 0, TimeSpan.Zero));

        var oldToken = _refreshTokens.Tokens.Single(t => t.TokenHash == _tokenService.HashRefreshToken(login.RefreshToken));
        var newToken = _refreshTokens.Tokens.Single(t => t.TokenHash == _tokenService.HashRefreshToken(refreshed.RefreshToken));
        oldToken.IsRevoked.Should().BeTrue();
        oldToken.ReplacedByTokenId.Should().Be(newToken.Id);
        newToken.FamilyId.Should().Be(oldToken.FamilyId);
    }

    [Fact]
    public async Task Refresh_reusing_a_rotated_token_fails_and_revokes_the_whole_family()
    {
        _users.Add("jane.doe", Password, "Analyst");
        var login = (await _service.LoginInternalAsync(new InternalLoginRequest("jane.doe", Password))).Session;
        var rotated = await _service.RefreshAsync(login!.RefreshToken);

        var reuse = await _service.RefreshAsync(login.RefreshToken);
        var afterReuse = await _service.RefreshAsync(rotated!.RefreshToken);

        reuse.Should().BeNull();
        afterReuse.Should().BeNull("the legitimate-looking newest token belongs to a compromised family");
    }

    [Fact]
    public async Task Refresh_with_expired_token_returns_null()
    {
        _users.Add("jane.doe", Password, "Analyst");
        var login = (await _service.LoginInternalAsync(new InternalLoginRequest("jane.doe", Password))).Session;
        _time.Advance(TimeSpan.FromDays(7));

        var refreshed = await _service.RefreshAsync(login!.RefreshToken);

        refreshed.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_with_unknown_token_returns_null()
    {
        var refreshed = await _service.RefreshAsync("not-a-real-token");

        refreshed.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_for_a_removed_user_returns_null()
    {
        var analyst = _users.Add("jane.doe", Password, "Analyst");
        var login = (await _service.LoginInternalAsync(new InternalLoginRequest("jane.doe", Password))).Session;
        _users.Remove(analyst.Id);

        var refreshed = await _service.RefreshAsync(login!.RefreshToken);

        refreshed.Should().BeNull();
    }

    // ---- logout ----

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        _users.Add("jane.doe", Password, "Analyst");
        var login = (await _service.LoginInternalAsync(new InternalLoginRequest("jane.doe", Password))).Session;

        await _service.LogoutAsync(login!.RefreshToken);

        (await _service.RefreshAsync(login.RefreshToken)).Should().BeNull();
    }

    [Fact]
    public async Task Logout_with_unknown_token_does_nothing()
    {
        var act = () => _service.LogoutAsync("not-a-real-token");

        await act.Should().NotThrowAsync();
    }

    // ---- inactive accounts ----

    [Fact]
    public async Task Login_of_inactive_account_reports_it_only_after_the_password_is_verified()
    {
        var customer = _users.Add(CustomerCnpj, Password, "Customer");
        await _users.SetActiveAsync(customer.Id, false);

        var wrongPassword = await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, "wrong"));
        var rightPassword = await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password));

        wrongPassword.Status.Should().Be(LoginStatus.InvalidCredentials);
        rightPassword.Status.Should().Be(LoginStatus.AccountInactive);
        rightPassword.Session.Should().BeNull();
        _refreshTokens.Tokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_of_active_account_succeeds()
    {
        _users.Add("jane.doe", Password, "Analyst");

        var result = await _service.LoginInternalAsync(new InternalLoginRequest("jane.doe", Password));

        result.Status.Should().Be(LoginStatus.Success);
        result.Session.Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_of_inactive_account_returns_null()
    {
        var customer = _users.Add(CustomerCnpj, Password, "Customer");
        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;
        await _users.SetActiveAsync(customer.Id, false);

        (await _service.RefreshAsync(session!.RefreshToken)).Should().BeNull();
    }

    // ---- temporary password ----

    [Fact]
    public async Task Login_with_temporary_password_flags_that_the_password_must_change()
    {
        _users.AddWithTemporaryPassword(CustomerCnpj, Password, "Customer");

        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;

        session!.Response.User.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task Login_with_regular_password_does_not_flag_password_change()
    {
        _users.Add(CustomerCnpj, Password, "Customer");

        var session = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;

        session!.Response.User.MustChangePassword.Should().BeFalse();
    }

    // ---- change password ----

    [Fact]
    public async Task ChangePassword_succeeds_clears_the_flag_and_returns_a_new_session()
    {
        var customer = _users.AddWithTemporaryPassword(CustomerCnpj, Password, "Customer");

        var outcome = await _service.ChangePasswordAsync(customer.Id, new ChangePasswordRequest(Password, "Brand-New-Pass-2"));

        outcome.Status.Should().Be(PasswordChangeStatus.Success);
        outcome.Session!.Response.User.MustChangePassword.Should().BeFalse();
        _users.PasswordOf(customer.Id).Should().Be("Brand-New-Pass-2");
    }

    [Fact]
    public async Task ChangePassword_revokes_every_previous_session()
    {
        var customer = _users.AddWithTemporaryPassword(CustomerCnpj, Password, "Customer");
        var firstDevice = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;
        var secondDevice = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;

        var outcome = await _service.ChangePasswordAsync(customer.Id, new ChangePasswordRequest(Password, "Brand-New-Pass-2"));

        (await _service.RefreshAsync(firstDevice!.RefreshToken)).Should().BeNull();
        (await _service.RefreshAsync(secondDevice!.RefreshToken)).Should().BeNull();
        (await _service.RefreshAsync(outcome.Session!.RefreshToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task ChangePassword_with_wrong_current_password_changes_nothing()
    {
        var customer = _users.AddWithTemporaryPassword(CustomerCnpj, Password, "Customer");
        var login = (await _service.LoginCustomerAsync(new CustomerLoginRequest(CustomerCnpj, Password))).Session;

        var outcome = await _service.ChangePasswordAsync(customer.Id, new ChangePasswordRequest("wrong", "Brand-New-Pass-2"));

        outcome.Status.Should().Be(PasswordChangeStatus.InvalidCurrentPassword);
        outcome.Session.Should().BeNull();
        _users.PasswordOf(customer.Id).Should().Be(Password);
        (await _service.RefreshAsync(login!.RefreshToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task ChangePassword_rejects_reusing_the_current_password()
    {
        var customer = _users.AddWithTemporaryPassword(CustomerCnpj, Password, "Customer");

        var outcome = await _service.ChangePasswordAsync(customer.Id, new ChangePasswordRequest(Password, Password));

        outcome.Status.Should().Be(PasswordChangeStatus.Rejected);
        outcome.Errors.Should().ContainSingle().Which.Should().Contain("different");
        (await _users.FindByIdAsync(customer.Id))!.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_passes_along_password_rule_errors()
    {
        var customer = _users.AddWithTemporaryPassword(CustomerCnpj, Password, "Customer");

        var outcome = await _service.ChangePasswordAsync(customer.Id, new ChangePasswordRequest(Password, "short"));

        outcome.Status.Should().Be(PasswordChangeStatus.Rejected);
        outcome.Errors.Should().Equal("Passwords must be at least 8 characters.");
    }

    // ---- current user ----

    [Fact]
    public async Task GetUser_returns_name_and_roles()
    {
        var analyst = _users.Add("jane.doe", Password, "Analyst", "Admin");

        var user = await _service.GetUserAsync(analyst.Id);

        user.Should().BeEquivalentTo(new { Id = analyst.Id, Name = "jane.doe name", Roles = new[] { "Analyst", "Admin" }, MustChangePassword = false });
    }

    [Fact]
    public async Task GetUser_for_unknown_id_returns_null()
    {
        (await _service.GetUserAsync(Guid.NewGuid())).Should().BeNull();
    }
}
