using BZ.Howly.Api.DAO.Context;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>Runs the real API in memory, swapping SQL Server for an in-memory SQLite database.</summary>
public class HowlyApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _filesRoot = Path.Combine(Path.GetTempPath(), "howly-api-tests", Guid.NewGuid().ToString("N"));

    protected virtual int LoginPermitLimit => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Howly"] = "unused-in-tests",
            ["Jwt:Issuer"] = "howly-api",
            ["Jwt:Audience"] = "howly-web",
            ["Jwt:SigningKey"] = "integration-test-signing-key-32-bytes-min!",
            ["RateLimiting:Login:PermitLimit"] = LoginPermitLimit.ToString(),
            ["FileStorage:RootPath"] = _filesRoot,
            ["OverdueSteps:Enabled"] = "false", // tests call the check themselves, with the time they need
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<HowlyDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<HowlyDbContext>>();

            _connection.Open();
            services.AddDbContext<HowlyDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HowlyDbContext>().Database.EnsureCreated();
    }

    public async Task<Guid> CreateUserAsync(string username, string password, string displayName, params string[] roles)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser { UserName = username, DisplayName = displayName };
        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));

        await userManager.AddToRolesAsync(user, roles);
        return user.Id;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
            if (Directory.Exists(_filesRoot))
                Directory.Delete(_filesRoot, recursive: true);
        }
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(descriptor);
    }
}
