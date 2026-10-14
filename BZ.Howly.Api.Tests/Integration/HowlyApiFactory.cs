using BZ.Howly.Api.Core.Tenancy;
using BZ.Howly.Api.DAO.Configurations;
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

    /// <summary>Where the stored files land (one folder per consultancy inside).</summary>
    public string FilesRoot => _filesRoot;

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
            ["Tenancy:FallbackSlug"] = TenantConfiguration.BiaSlug, // plain "localhost" clients work in the "bia" consultancy
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
        EnsureDatabase();
    }

    /// <summary>The schema (with the seeded tenants) exists before any client or helper touches it.</summary>
    private void EnsureDatabase()
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HowlyDbContext>().Database.EnsureCreated();
    }

    /// <summary>A client for another portal (<c>{slug}.localhost</c>); the plain clients use the "bia" consultancy.</summary>
    public HttpClient CreatePortalClient(string slug)
    {
        var client = CreateClient(new() { HandleCookies = false, BaseAddress = new Uri($"http://{slug}.localhost") });
        return client;
    }

    /// <summary>A consultancy of its own, for the isolation tests (the database seeds only "bia" and the platform).</summary>
    public async Task<Tenant> CreateTenantAsync(string slug, string name, TenantStatus status = TenantStatus.Active)
    {
        EnsureDatabase();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<HowlyDbContext>();
        var tenant = Tenant.Create(Guid.NewGuid(), slug, name, DateTimeOffset.UtcNow);
        if (status == TenantStatus.Suspended)
            tenant.Suspend();
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    /// <summary>A service scope working inside a consultancy, like a request to its portal would.</summary>
    public IServiceScope CreateTenantScope(string slug = TenantConfiguration.BiaSlug)
    {
        EnsureDatabase();
        var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<HowlyDbContext>();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(context.Tenants.AsNoTracking().Single(t => t.Slug == slug));
        return scope;
    }

    public Task<Guid> CreateUserAsync(string username, string password, string displayName, params string[] roles) =>
        CreateUserInAsync(TenantConfiguration.BiaSlug, username, password, displayName, roles);

    public async Task<Guid> CreateUserInAsync(string tenantSlug, string username, string password, string displayName, params string[] roles)
    {
        using var scope = CreateTenantScope(tenantSlug);
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
