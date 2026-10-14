using System.Text;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Utils.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BZ.Howly.Api.Tests.Identity;

public class JwtTokenServiceTests
{
    private const string SigningKey = "test-signing-key-with-at-least-32-bytes!";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserAccount Analyst = new(
        Guid.Parse("9b6f1c1e-2f4a-4c55-9d0e-7a1b2c3d4e5f"), "jane.doe", "Jane Doe", ["Analyst", "Admin"]);

    private static JwtTokenService CreateService(string signingKey = SigningKey) =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "howly-api",
            Audience = "howly-web",
            SigningKey = signingKey,
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
            RefreshTokenLifetime = TimeSpan.FromDays(7),
        }), new FakeTimeProvider(Now));

    [Fact]
    public async Task CreateAccessToken_produces_a_token_signed_for_the_configured_issuer_and_audience()
    {
        var token = CreateService().CreateAccessToken(Analyst);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, new TokenValidationParameters
        {
            ValidIssuer = "howly-api",
            ValidAudience = "howly-web",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateLifetime = false,
        });

        result.IsValid.Should().BeTrue(result.Exception?.Message);
    }

    [Fact]
    public void CreateAccessToken_carries_user_id_name_and_every_role()
    {
        var token = new JsonWebToken(CreateService().CreateAccessToken(Analyst).Value);

        token.Subject.Should().Be("9b6f1c1e-2f4a-4c55-9d0e-7a1b2c3d4e5f");
        token.GetClaim("name").Value.Should().Be("Jane Doe");
        token.Claims.Where(c => c.Type == "role").Select(c => c.Value)
            .Should().BeEquivalentTo("Analyst", "Admin");
    }

    [Fact]
    public void CreateAccessToken_marks_users_that_must_change_their_password()
    {
        var token = new JsonWebToken(CreateService().CreateAccessToken(Analyst with { MustChangePassword = true }).Value);

        token.GetClaim("pwd_change_required").Value.Should().Be("true");
    }

    [Fact]
    public void CreateAccessToken_marks_customers_that_must_confirm_their_data()
    {
        var token = new JsonWebToken(CreateService().CreateAccessToken(Analyst with { MustConfirmProfile = true }).Value);

        token.GetClaim("profile_confirmation_required").Value.Should().Be("true");
    }

    [Fact]
    public void CreateAccessToken_omits_the_confirmation_claim_for_regular_users()
    {
        var token = new JsonWebToken(CreateService().CreateAccessToken(Analyst).Value);

        token.TryGetClaim("profile_confirmation_required", out _).Should().BeFalse();
    }

    [Fact]
    public void CreateAccessToken_omits_the_password_change_claim_for_regular_users()
    {
        var token = new JsonWebToken(CreateService().CreateAccessToken(Analyst).Value);

        token.TryGetClaim("pwd_change_required", out _).Should().BeFalse();
    }

    [Fact]
    public void CreateAccessToken_expires_after_the_configured_lifetime()
    {
        var token = CreateService().CreateAccessToken(Analyst);

        token.ExpiresAt.Should().Be(new DateTimeOffset(2026, 10, 13, 12, 15, 0, TimeSpan.Zero));
        new JsonWebToken(token.Value).ValidTo.Should().Be(new DateTime(2026, 10, 13, 12, 15, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CreateRefreshToken_generates_a_unique_value_each_time()
    {
        var service = CreateService();

        service.CreateRefreshToken().Value.Should().NotBe(service.CreateRefreshToken().Value);
    }

    [Fact]
    public void CreateRefreshToken_expires_after_the_configured_lifetime()
    {
        CreateService().CreateRefreshToken().ExpiresAt
            .Should().Be(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void CreateRefreshToken_hash_matches_hashing_the_value_and_differs_from_it()
    {
        var service = CreateService();
        var refresh = service.CreateRefreshToken();

        refresh.Hash.Should().Be(service.HashRefreshToken(refresh.Value));
        refresh.Hash.Should().NotBe(refresh.Value);
    }

    [Fact]
    public void HashRefreshToken_is_sha256_hex()
    {
        // SHA-256("abc"), RFC 6234 test vector
        CreateService().HashRefreshToken("abc")
            .Should().Be("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
    }

    [Fact]
    public void Constructor_rejects_signing_key_shorter_than_32_bytes()
    {
        var act = () => CreateService("too-short-key");

        act.Should().Throw<ArgumentException>().WithMessage("*32 bytes*");
    }
}
