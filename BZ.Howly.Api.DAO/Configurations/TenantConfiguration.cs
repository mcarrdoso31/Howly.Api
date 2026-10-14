using BZ.Howly.Api.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

/// <summary>
/// The consultancies. Seeds the two tenants of the MVP through migrations: the platform portal and "Bia", the only
/// consultancy for now. Ids and dates are constant so migrations stay stable.
/// </summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public static readonly Guid PlatformId = Guid.Parse("0e0a7c3e-5f2b-4c1d-9a00-000000000001");
    public static readonly Guid BiaId = Guid.Parse("0e0a7c3e-5f2b-4c1d-9a00-0000000000b1");
    public const string BiaSlug = "bia";

    private static readonly DateTimeOffset SeededAt = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Slug).HasMaxLength(Tenant.MaxSlugLength).IsUnicode(false).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(Tenant.MaxNameLength).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasData(
            new { Id = PlatformId, Slug = Tenant.PlatformSlug, Name = "Howly", Status = TenantStatus.Active, IsPlatform = true, CreatedAt = SeededAt },
            new { Id = BiaId, Slug = BiaSlug, Name = "Bia Consultoria Ambiental", Status = TenantStatus.Active, IsPlatform = false, CreatedAt = SeededAt });
    }
}
