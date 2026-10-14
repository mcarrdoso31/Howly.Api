using BZ.Howly.Api.Core.Notifications;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.DAO.Context;
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

        // The bell asks "mine, newest first" (and counts the unread ones); the timeline asks "this process, this person".
        const string tenant = HowlyDbContext.TenantIdProperty;
        builder.HasIndex(tenant, nameof(Notification.UserId), nameof(Notification.CreatedAt)).IncludeProperties(n => n.ReadAt);
        builder.HasIndex(tenant, nameof(Notification.UserId), nameof(Notification.ReadAt));
        // One notification per person and event: a repeated one would break the "seen by the customer" lookup.
        builder.HasIndex(tenant, nameof(Notification.ProcessId), nameof(Notification.UserId), nameof(Notification.EventId)).IsUnique();

        builder.HasOne<AppUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        // The history of a process is never deleted along with it.
        builder.HasOne<Process>().WithMany().HasForeignKey(n => n.ProcessId).OnDelete(DeleteBehavior.Restrict);
    }
}
