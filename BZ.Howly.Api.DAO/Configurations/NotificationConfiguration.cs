using BZ.Howly.Api.Core.Notifications;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.ProcessTitle).HasMaxLength(Process.MaxTitleLength).IsRequired();
        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(40);
        builder.Property(n => n.StepName).HasMaxLength(ProcessType.MaxNameLength);
        builder.Property(n => n.Details).HasMaxLength(Process.MaxCommentLength);
        builder.Property(n => n.ActorName).HasMaxLength(200).IsRequired();

        // The bell asks "my unread ones, newest first"; the timeline asks "this process, this person".
        builder.HasIndex(n => new { n.UserId, n.ReadAt });
        builder.HasIndex(n => new { n.ProcessId, n.UserId });

        builder.HasOne<AppUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Process>().WithMany().HasForeignKey(n => n.ProcessId).OnDelete(DeleteBehavior.Cascade);
    }
}
