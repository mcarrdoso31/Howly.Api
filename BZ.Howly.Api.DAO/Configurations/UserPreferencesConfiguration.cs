using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

public sealed class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.ToTable("UserPreferences");
        builder.HasKey(p => p.UserId);

        builder.Property(p => p.ThemeMode).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(p => p.ThemePreset).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(p => p.PrimaryColor).HasMaxLength(7).IsUnicode(false);
        builder.Property(p => p.Language).HasMaxLength(10).IsUnicode(false).IsRequired();

        // At most three ids, comma separated (3 × 36 + 2 characters): small, and only ever read whole.
        builder.Property(p => p.PinnedProcessIds)
            .HasColumnName("PinnedProcesses")
            .HasMaxLength(110)
            .IsUnicode(false)
            .IsRequired()
            .HasConversion(
                ids => string.Join(',', ids),
                text => text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList(),
                new ValueComparer<IReadOnlyList<Guid>>(
                    (a, b) => a!.SequenceEqual(b!),
                    ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                    ids => ids.ToList()));

        builder.HasOne<AppUser>()
            .WithOne()
            .HasForeignKey<UserPreferences>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
