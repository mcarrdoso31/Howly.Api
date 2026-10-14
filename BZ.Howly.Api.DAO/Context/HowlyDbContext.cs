using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Notifications;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared.Exceptions;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BZ.Howly.Api.DAO.Context;

public sealed class HowlyDbContext(DbContextOptions<HowlyDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<ProcessType> ProcessTypes => Set<ProcessType>();
    public DbSet<Process> Processes => Set<Process>();
    public DbSet<ProcessDocument> ProcessDocuments => Set<ProcessDocument>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HowlyDbContext).Assembly);

        // SQLite (used by the integration tests) cannot compare nor sort DateTimeOffset values; store them as numbers there.
        // SQL Server and PostgreSQL keep their native types, so migrations are not affected.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            foreach (var property in builder.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
        }
    }

    /// <summary>
    /// Someone else saved the same process or document first: its <c>Version</c> changed since it was read, or the new
    /// timeline event / document version took a number already used. Answered as 409 so the client reloads. Identity
    /// keeps handling its own concurrency failures.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsConcurrentChange(exception))
        {
            throw new ConflictException("This process was changed by someone else in the meantime. Reload it and try again.", "concurrency_conflict");
        }
    }

    private static bool IsConcurrentChange(DbUpdateException exception) => exception is DbUpdateConcurrencyException
        ? exception.Entries.Any(e => e.Entity is Process or ProcessDocument)
        // Only the (process, sequence) and (document, number) unique indexes can refuse these inserts.
        : exception.Entries.Any(e => e.State == EntityState.Added && e.Entity is ProcessEvent or DocumentVersion);
}
