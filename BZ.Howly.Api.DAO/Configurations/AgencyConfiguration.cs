using BZ.Howly.Api.Core.Agencies;
using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

public sealed class AgencyConfiguration : IEntityTypeConfiguration<Agency>
{
    public void Configure(EntityTypeBuilder<Agency> builder)
    {
        builder.ToTable("Agencies");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Acronym).HasMaxLength(Agency.MaxAcronymLength).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(Agency.MaxNameLength).IsRequired();
        builder.Property(a => a.Sphere).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.State).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        builder.Property(a => a.City).HasMaxLength(Agency.MaxCityLength);
        builder.Property(a => a.Website).HasMaxLength(Agency.MaxWebsiteLength);
        builder.Property(a => a.ProtocolSystem).HasMaxLength(Agency.MaxProtocolSystemLength);
        builder.Ignore(a => a.DisplayName);
        // The same acronym exists in several states (SEMA, SEMAD…): searched, not unique.
        builder.HasIndex(HowlyDbContext.TenantIdProperty, nameof(Agency.Acronym));
    }
}
