using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.DAO.Context;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

/// <summary>Howly columns of the Identity user table (the Identity ones keep their defaults).</summary>
public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.DisplayName).HasMaxLength(StaffProfileChanges.MaxDisplayNameLength);
        builder.Property(u => u.Nickname).HasMaxLength(StaffProfileChanges.MaxNicknameLength);
        builder.Property(u => u.SecondaryEmail).HasMaxLength(256);
        builder.Property(u => u.SecondaryPhone).HasMaxLength(11).IsUnicode(false);
        builder.Property(u => u.AvatarKey).HasMaxLength(150).IsUnicode(false);

        // A login (username or CNPJ) is unique per consultancy: the same CNPJ can be a customer of two of them.
        // Identity's own index makes it unique across the whole database, so it is replaced.
        var identityIndex = builder.Metadata.GetIndexes().Single(i => i.GetDatabaseName() == "UserNameIndex");
        builder.Metadata.RemoveIndex(identityIndex);
        builder.HasIndex(HowlyDbContext.TenantIdProperty, nameof(AppUser.NormalizedUserName)).IsUnique().HasDatabaseName("UserNameIndex");
    }
}
