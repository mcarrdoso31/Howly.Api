using BZ.Howly.Api.Core.Agencies;
using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.DAO.Context;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

public sealed class ProcessConfiguration : IEntityTypeConfiguration<Process>
{
    private const int EnumLength = 20;

    public void Configure(EntityTypeBuilder<Process> builder)
    {
        builder.ToTable("Processes");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Title).HasMaxLength(Process.MaxTitleLength).IsRequired();
        builder.Property(p => p.ProcessTypeName).HasMaxLength(ProcessType.MaxNameLength).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(EnumLength);
        builder.Property(p => p.StatusReason).HasMaxLength(Process.MaxCommentLength);
        builder.Property(p => p.WaitingOn).HasConversion<string>().HasMaxLength(EnumLength);
        builder.Property(p => p.ActiveStepName).HasMaxLength(ProcessType.MaxNameLength);
        builder.Property(p => p.Version).IsConcurrencyToken();
        builder.Ignore(p => p.ActiveStep);

        // Every index starts with the tenant, like every query. Lists come newest first (also per customer, for the
        // portal); the overdue job asks for in-progress processes by deadline.
        const string tenant = HowlyDbContext.TenantIdProperty;
        builder.HasIndex(tenant, nameof(Process.UpdatedAt));
        builder.HasIndex(tenant, nameof(Process.CustomerId), nameof(Process.UpdatedAt));
        builder.HasIndex(tenant, nameof(Process.AssigneeId));
        builder.HasIndex(tenant, nameof(Process.ParentId));
        builder.HasIndex(tenant, nameof(Process.Status), nameof(Process.WaitingOn));
        builder.HasIndex(tenant, nameof(Process.Status), nameof(Process.ActiveStepDueAt));

        builder.HasOne<Customer>().WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProcessType>().WithMany().HasForeignKey(p => p.ProcessTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(p => p.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Process>().WithMany().HasForeignKey(p => p.ParentId).OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(p => p.Steps, step =>
        {
            step.ToTable("ProcessSteps");
            step.WithOwner().HasForeignKey("ProcessId");
            step.HasKey(s => s.Id);
            step.Property(s => s.Id).ValueGeneratedNever();
            step.Property(s => s.Name).HasMaxLength(ProcessType.MaxNameLength).IsRequired();
            step.Property(s => s.Description).HasMaxLength(ProcessType.MaxDescriptionLength);
            step.Property(s => s.Owner).HasConversion<string>().HasMaxLength(EnumLength);
            step.Property(s => s.Status).HasConversion<string>().HasMaxLength(EnumLength);
            step.Property(s => s.AgencyName).HasMaxLength(ProcessType.MaxNameLength);
            step.HasOne<Agency>().WithMany().HasForeignKey(s => s.AgencyId).OnDelete(DeleteBehavior.Restrict);
            step.Property(s => s.CustomerChecklist).HasMaxLength(ProcessType.MaxChecklistLength);
            step.Property(s => s.DeliveryChecklist).HasMaxLength(ProcessType.MaxChecklistLength);
            step.Ignore(s => s.DueAt);
            step.Property(s => s.OverdueNotifiedAt);
            // As ticks: SQL Server's time type stops at 24 hours, and pauses last days.
            step.Property(s => s.PausedFor).HasConversion<long>();
        });
        builder.Navigation(p => p.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(p => p.Events, @event =>
        {
            @event.ToTable("ProcessEvents");
            @event.WithOwner().HasForeignKey("ProcessId");
            @event.HasKey(e => e.Id);
            @event.Property(e => e.Id).ValueGeneratedNever();
            @event.Property(e => e.Type).HasConversion<string>().HasMaxLength(40);
            @event.Property(e => e.StepName).HasMaxLength(ProcessType.MaxNameLength);
            @event.Property(e => e.Comment).HasMaxLength(Process.MaxCommentLength);
            @event.Property(e => e.Details).HasMaxLength(Process.MaxCommentLength);
            @event.Property(e => e.ActorName).HasMaxLength(200).IsRequired();
            @event.HasIndex("ProcessId", nameof(ProcessEvent.Sequence)).IsUnique();
        });
        builder.Navigation(p => p.Events).HasField("_events").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
