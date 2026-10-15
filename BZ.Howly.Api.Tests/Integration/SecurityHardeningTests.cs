using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.DAO.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>The API counting password checks and keeping what it logs, to see what a request costs and leaves behind.</summary>
public sealed class ObservedApiFactory : HowlyApiFactory
{
    public CountingPasswordHasher Hasher { get; } = new();
    public CapturedLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHasher<AppUser>>();
            services.AddSingleton<IPasswordHasher<AppUser>>(Hasher);
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }
}

public sealed class CountingPasswordHasher : PasswordHasher<AppUser>
{
    private int _verifications;
    public int Verifications => _verifications;

    public override PasswordVerificationResult VerifyHashedPassword(AppUser user, string hashedPassword, string providedPassword)
    {
        Interlocked.Increment(ref _verifications);
        return base.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}

public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();
    public IEnumerable<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new Logger(_lines);
    public void Dispose() { }

    private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue(formatter(state, exception));
    }
}

/// <summary>
/// What an attacker or a log reader could learn: the sign-in takes the same time whether the account exists or not,
/// the logs keep no CPF/CNPJ nor file names, and every endpoint asks for a sign-in unless it says otherwise.
/// </summary>
public class SecurityHardeningTests(ObservedApiFactory factory) : IClassFixture<ObservedApiFactory>
{
    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"7{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    [Fact]
    public async Task Signing_in_as_someone_who_does_not_exist_checks_a_password_all_the_same()
    {
        var client = factory.CreateClient();
        var before = factory.Hasher.Verifications;

        var response = await client.PostAsJsonAsync("/api/auth/internal/login", new { username = NewUsername("ghost"), password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Hasher.Verifications.Should().BeGreaterThan(before, "an unknown account must cost what a known one costs");
    }

    [Fact]
    public async Task Signing_in_through_the_wrong_portal_checks_a_password_all_the_same()
    {
        var client = factory.CreateClient();
        var customerLogin = NewCnpj();
        await factory.CreateUserAsync(customerLogin, Password, "Beagle Ltda", Roles.Customer);
        var before = factory.Hasher.Verifications;

        var response = await client.PostAsJsonAsync("/api/auth/internal/login", new { username = customerLogin, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Hasher.Verifications.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task Creating_a_customer_does_not_log_its_document()
    {
        var client = factory.CreateClient();
        var analyst = NewUsername("analyst");
        await factory.CreateUserAsync(analyst, Password, "Jane Analyst", Roles.Analyst);
        var staff = await client.LoginInternalAsync(analyst);
        var cnpj = NewCnpj();

        var response = await client.SendAsync(HttpMethod.Post, "/api/customers", staff, new
        {
            document = cnpj,
            companyName = "Beagle",
            contacts = new[] { new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.Logs.Lines.Should().NotContain(line => line.Contains(cnpj));
    }

    [Fact]
    public async Task A_refused_upload_does_not_log_the_file_name()
    {
        var client = factory.CreateClient();
        var analyst = NewUsername("analyst");
        await factory.CreateUserAsync(analyst, Password, "Jane Analyst", Roles.Analyst);
        var staff = await client.LoginInternalAsync(analyst);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("MZ-not-really"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "RG_Joao_da_Silva.exe");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/process-types/examples") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staff);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Logs.Lines.Should().Contain(line => line.Contains("Refused upload"));
        factory.Logs.Lines.Should().NotContain(line => line.Contains("Joao"));
    }

    [Fact]
    public void An_endpoint_that_says_nothing_asks_for_a_sign_in()
    {
        var fallback = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy;

        fallback.Should().NotBeNull();
        fallback!.Requirements.Should().ContainSingle(r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/api/tenant")]
    public async Task The_public_endpoints_stay_open(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
