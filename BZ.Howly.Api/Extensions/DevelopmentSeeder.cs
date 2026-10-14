using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.DAO.Context;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.Extensions;

/// <summary>
/// Development only: applies migrations and creates sample users whose credentials come from user-secrets
/// (Seed:Admin:* and Seed:Customer:*). Nothing is created when a setting is missing.
/// </summary>
public static class DevelopmentSeeder
{
    public static async Task SeedDevelopmentDataAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DevelopmentSeeder));
        var config = app.Configuration;

        await services.GetRequiredService<HowlyDbContext>().Database.MigrateAsync();

        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        await SeedUserAsync(userManager, logger,
            config["Seed:Admin:Username"], config["Seed:Admin:Password"], config["Seed:Admin:DisplayName"] ?? "Howly Admin",
            Roles.Admin);

        var customerUsername = Cnpj.TryParse(config["Seed:Customer:Cnpj"], out var cnpj) ? cnpj!.Value : null;
        await SeedUserAsync(userManager, logger,
            customerUsername, config["Seed:Customer:Password"], config["Seed:Customer:DisplayName"] ?? "Sample Customer",
            Roles.Customer);
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
