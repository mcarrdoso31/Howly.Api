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
        builder.Property(t => t.LogoKey).HasMaxLength(150).IsUnicode(false);
        builder.Ignore(t => t.LogoVersion);

        // The profile lives in the tenant row itself (Profile_* columns): always read together, changed rarely.
        builder.OwnsOne(t => t.Profile, profile =>
        {
            profile.Property(p => p.LegalName).HasMaxLength(TenantProfile.MaxLegalNameLength);
            profile.Property(p => p.Cnpj).HasMaxLength(14).IsFixedLength().IsUnicode(false);
            profile.Property(p => p.Email).HasMaxLength(TenantProfile.MaxEmailLength);
            profile.Property(p => p.Phone).HasMaxLength(11).IsUnicode(false);
            profile.Property(p => p.PostalCode).HasMaxLength(8).IsFixedLength().IsUnicode(false);
            profile.Property(p => p.Street).HasMaxLength(TenantProfile.MaxStreetLength);
            profile.Property(p => p.City).HasMaxLength(TenantProfile.MaxCityLength);
            profile.Property(p => p.State).HasMaxLength(2).IsFixedLength().IsUnicode(false);
            profile.Property(p => p.TimeZone).HasMaxLength(64).IsUnicode(false).IsRequired();
            profile.HasData(
                new { TenantId = PlatformId, TimeZone = TenantProfile.DefaultTimeZone },
                new { TenantId = BiaId, TimeZone = TenantProfile.DefaultTimeZone });
        });
        builder.Navigation(t => t.Profile).IsRequired();

        builder.HasData(
            new { Id = PlatformId, Slug = Tenant.PlatformSlug, Name = "Howly", Status = TenantStatus.Active, IsPlatform = true, CreatedAt = SeededAt },
            new { Id = BiaId, Slug = BiaSlug, Name = "Bia Consultoria Ambiental", Status = TenantStatus.Active, IsPlatform = false, CreatedAt = SeededAt });
    }
}
