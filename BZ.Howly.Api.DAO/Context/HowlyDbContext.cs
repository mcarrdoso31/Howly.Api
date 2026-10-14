using System.Linq.Expressions;
using System.Reflection;
using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Notifications;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared.Exceptions;
using BZ.Howly.Api.Core.Tenancy;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BZ.Howly.Api.DAO.Context;

/// <summary>
/// One database for every consultancy. Each tenant-owned table carries a <c>TenantId</c>: queries only ever see the
/// rows of the current tenant (<see cref="ITenantContext"/>) and inserts are stamped with it, so no code path has to
/// remember to filter.
/// </summary>
public sealed class HowlyDbContext(DbContextOptions<HowlyDbContext> options, ITenantContext tenantContext)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public const string TenantIdProperty = "TenantId";

    /// <summary>
    /// Everything that belongs to a consultancy. Owned parts (steps, events, versions, contacts) live in their owner's
    /// rows and go with it; roles are global names.
    /// </summary>
    private static readonly Type[] TenantOwnedTypes =
    [
        typeof(AppUser), typeof(Customer), typeof(UserPreferences), typeof(RefreshToken),
        typeof(ProcessType), typeof(Process), typeof(ProcessDocument), typeof(Notification),
    ];

    private static readonly PropertyInfo CurrentTenantIdProperty =
        typeof(HowlyDbContext).GetProperty(nameof(CurrentTenantId), BindingFlags.Instance | BindingFlags.NonPublic)!;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<ProcessType> ProcessTypes => Set<ProcessType>();
    public DbSet<Process> Processes => Set<Process>();
    public DbSet<ProcessDocument> ProcessDocuments => Set<ProcessDocument>();
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>Read by the query filters on every query; without a tenant nothing matches.</summary>
    private Guid CurrentTenantId => tenantContext.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Before the configurations: their indexes start with TenantId.
        foreach (var type in TenantOwnedTypes)
        {
            var entity = builder.Entity(type);
            entity.Property<Guid>(TenantIdProperty).IsRequired();
            entity.HasOne(typeof(Tenant)).WithMany().HasForeignKey(TenantIdProperty).OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(TenantFilter(type));
        }

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

    /// <summary><c>e => EF.Property&lt;Guid&gt;(e, "TenantId") == this.CurrentTenantId</c>, evaluated per query.</summary>
    private LambdaExpression TenantFilter(Type type)
    {
        var entity = Expression.Parameter(type, "e");
        var tenantId = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(Guid)], entity, Expression.Constant(TenantIdProperty));
        var current = Expression.Property(Expression.Constant(this), CurrentTenantIdProperty);
        return Expression.Lambda(Expression.Equal(tenantId, current), entity);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Stamps new rows with the current tenant. Someone else saved the same process or document first: its
    /// <c>Version</c> changed since it was read, or the new timeline event / document version took a number already
    /// used. Answered as 409 so the client reloads. Identity keeps handling its own concurrency failures.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenant();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsConcurrentChange(exception))
        {
            throw new ConflictException("This process was changed by someone else in the meantime. Reload it and try again.", "concurrency_conflict");
        }
    }

    /// <summary>New tenant rows get the current tenant; writing them without one, or into another tenant, is a bug.</summary>
    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Added && TenantOwnedTypes.Contains(e.Metadata.ClrType)))
        {
            var tenantId = tenantContext.TenantId
                ?? throw new InvalidOperationException($"A {entry.Metadata.ClrType.Name} cannot be saved without a tenant.");
            var property = entry.Property(TenantIdProperty);
            if (property.CurrentValue is Guid current && current != Guid.Empty && current != tenantId)
                throw new InvalidOperationException($"A {entry.Metadata.ClrType.Name} of another tenant cannot be saved here.");
            property.CurrentValue = tenantId;
        }
    }

    private static bool IsConcurrentChange(DbUpdateException exception) => exception is DbUpdateConcurrencyException
        ? exception.Entries.Any(e => e.Entity is Process or ProcessDocument)
        // Only the (process, sequence) and (document, number) unique indexes can refuse these inserts.
        : exception.Entries.Any(e => e.State == EntityState.Added && e.Entity is ProcessEvent or DocumentVersion);
}
