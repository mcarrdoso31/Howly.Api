using BZ.Howly.Api.Core.Identity;
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
        builder.Property(u => u.AvatarKey).HasMaxLength(100).IsUnicode(false);
    }
}
