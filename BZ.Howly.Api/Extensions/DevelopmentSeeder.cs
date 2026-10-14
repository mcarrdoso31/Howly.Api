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
/// Development only: applies migrations and creates sample users whose credentials come from user-secrets: in the
/// "bia" consultancy Seed:Admin:* and Seed:Customer:*, in the platform portal Seed:PlatformAdmin:*. Nothing is created
/// when a setting is missing.
/// </summary>
public static class DevelopmentSeeder
{
    public static async Task SeedDevelopmentDataAsync(this WebApplication app)
    {
        var config = app.Configuration;
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<HowlyDbContext>().Database.MigrateAsync();

        await InTenantAsync(app, TenantConfiguration.BiaSlug, async (userManager, logger) =>
        {
            await SeedUserAsync(userManager, logger,
                config["Seed:Admin:Username"], config["Seed:Admin:Password"], config["Seed:Admin:DisplayName"] ?? "Howly Admin",
                Roles.Admin);

            var customerUsername = Cnpj.TryParse(config["Seed:Customer:Cnpj"], out var cnpj) ? cnpj!.Value : null;
            await SeedUserAsync(userManager, logger,
                customerUsername, config["Seed:Customer:Password"], config["Seed:Customer:DisplayName"] ?? "Sample Customer",
                Roles.Customer);
        });

        await InTenantAsync(app, Tenant.PlatformSlug, (userManager, logger) =>
            SeedUserAsync(userManager, logger,
                config["Seed:PlatformAdmin:Username"], config["Seed:PlatformAdmin:Password"], config["Seed:PlatformAdmin:DisplayName"] ?? "Howly Platform",
                Roles.PlatformAdmin));
    }

    /// <summary>Accounts belong to a consultancy: each seed runs in a scope of its own tenant.</summary>
    private static async Task InTenantAsync(WebApplication app, string slug, Func<UserManager<AppUser>, ILogger, Task> seed)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var tenant = await services.GetRequiredService<ITenantRepository>().FindBySlugAsync(slug)
            ?? throw new InvalidOperationException($"The tenant \"{slug}\" is missing; the migrations seed it.");
        services.GetRequiredService<ITenantContext>().Set(tenant);
        await seed(services.GetRequiredService<UserManager<AppUser>>(), services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DevelopmentSeeder)));
    }

    private static async Task SeedUserAsync(
        UserManager<AppUser> userManager, ILogger logger, string? username, string? password, string displayName, string role)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Seed for role {Role} skipped: username/CNPJ or password not configured.", role);
            return;
        }

        if (await userManager.FindByNameAsync(username) is not null)
            return;

        var user = new AppUser { UserName = username, DisplayName = displayName };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogWarning("Seed for role {Role} failed: {Errors}", role, string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, role);
        logger.LogInformation("Seeded {Role} user {Username}.", role, username);
    }
}
