using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Core.Tenancy;
using BZ.Howly.Api.DAO.Configurations;
using BZ.Howly.Api.DAO.Context;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.Extensions;

/// <summary>
/// Development only: applies migrations and leaves the "bia" consultancy ready to try, from user-secrets:
/// <list type="bullet">
/// <item>Seed:Admin:* (Username, Password, DisplayName): an Admin;</item>
/// <item>Seed:Analyst:* (Username, Password, DisplayName): an Analyst;</item>
/// <item>Seed:Customer:* (Cnpj, Password, DisplayName, and optionally ContactName, ContactEmail, ContactPhone): a
/// complete sample customer (company, contact and portal login);</item>
/// <item>Seed:PlatformAdmin:* in the platform portal.</item>
/// </list>
/// Each one is skipped when its settings are missing, and nothing is created twice.
/// </summary>
public static class DevelopmentSeeder
{
    public static async Task SeedDevelopmentDataAsync(this WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HowlyDbContext>().Database.MigrateAsync();

        await SeedAsync(app.Services, app.Configuration);
    }

    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        await InTenantAsync(services, TenantConfiguration.BiaSlug, async scope =>
        {
            var admin = await SeedUserAsync(scope, config, "Admin", Roles.Admin, "Howly Admin");
            await SeedUserAsync(scope, config, "Analyst", Roles.Analyst, "Sample Analyst");
            await SeedCustomerAsync(scope, config, createdBy: admin?.Id ?? Guid.Empty);
        });

        await InTenantAsync(services, Tenant.PlatformSlug, scope => SeedUserAsync(scope, config, "PlatformAdmin", Roles.PlatformAdmin, "Howly Platform"));
    }

    /// <summary>Accounts belong to a consultancy: each seed runs in a scope of its own tenant.</summary>
    private static async Task InTenantAsync(IServiceProvider root, string slug, Func<IServiceProvider, Task> seed)
    {
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await services.GetRequiredService<ITenantRepository>().FindBySlugAsync(slug)
            ?? throw new InvalidOperationException($"The tenant \"{slug}\" is missing; the migrations seed it.");
        services.GetRequiredService<ITenantContext>().Set(tenant);
        await seed(services);
    }

    /// <summary>The account of Seed:{section}:*, created when missing; null when not configured.</summary>
    private static async Task<AppUser?> SeedUserAsync(
        IServiceProvider services, IConfiguration config, string section, string role, string defaultName, string? username = null)
    {
        var logger = Logger(services);
        username ??= config[$"Seed:{section}:Username"];
        var password = config[$"Seed:{section}:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Seed of {Section} skipped: username/CNPJ or password not configured.", section);
            return null;
        }

        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        if (await userManager.FindByNameAsync(username) is { } existing)
            return existing;

        var user = new AppUser { UserName = username, DisplayName = config[$"Seed:{section}:DisplayName"] ?? defaultName };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogWarning("Seed of {Section} failed: {Errors}", section, string.Join("; ", result.Errors.Select(e => e.Description)));
            return null;
        }

        await userManager.AddToRoleAsync(user, role);
        logger.LogInformation("Seeded {Role} {Username}.", role, username);
        return user;
    }

    /// <summary>
    /// A complete customer: the company with a contact that receives notices, plus its portal login (the CNPJ). A login
    /// left without its company by an older seed gets the company now.
    /// </summary>
    private static async Task SeedCustomerAsync(IServiceProvider services, IConfiguration config, Guid createdBy)
    {
        if (!Cnpj.TryParse(config["Seed:Customer:Cnpj"], out var cnpj))
        {
            Logger(services).LogInformation("Seed of Customer skipped: Seed:Customer:Cnpj missing or invalid.");
            return;
        }

        var user = await SeedUserAsync(services, config, "Customer", Roles.Customer, "Sample Customer", username: cnpj!.Value);
        var customers = services.GetRequiredService<ICustomerRepository>();
        if (user is null || await customers.FindByUserIdAsync(user.Id) is not null)
            return;

        var companyName = config["Seed:Customer:DisplayName"] ?? "Sample Customer";
        ContactDetails[] contacts =
        [
            new(config["Seed:Customer:ContactName"] ?? "Contato principal",
                config["Seed:Customer:ContactEmail"] ?? "contato@empresa.example",
                config["Seed:Customer:ContactPhone"] ?? "(11) 91234-5678",
                ReceivesNotices: true),
        ];
        var errors = Customer.Validate(companyName, contacts);
        if (errors.Count > 0)
        {
            Logger(services).LogWarning("Seed of Customer failed: {Fields}", string.Join(", ", errors.Keys));
            return;
        }

        await customers.AddAsync(Customer.Create(cnpj, companyName, contacts, user.Id, createdBy, DateTimeOffset.UtcNow));
        await customers.SaveChangesAsync();
        Logger(services).LogInformation("Seeded customer company {Company}.", companyName);
    }

    private static ILogger Logger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DevelopmentSeeder));
}
