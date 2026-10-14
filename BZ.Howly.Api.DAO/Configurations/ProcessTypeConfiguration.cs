using BZ.Howly.Api.Core.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

public sealed class ProcessTypeConfiguration : IEntityTypeConfiguration<ProcessType>
{
    public void Configure(EntityTypeBuilder<ProcessType> builder)
    {
        builder.ToTable("ProcessTypes");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(ProcessType.MaxNameLength).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(ProcessType.MaxDescriptionLength);
        builder.HasIndex(t => t.Name);

        builder.OwnsMany(t => t.Steps, step =>
        {
            step.ToTable("ProcessTypeSteps");
            step.WithOwner().HasForeignKey("ProcessTypeId");
            step.HasKey(s => s.Id);
            step.Property(s => s.Id).ValueGeneratedNever();
            step.Property(s => s.Name).HasMaxLength(ProcessType.MaxNameLength).IsRequired();
            step.Property(s => s.Description).HasMaxLength(ProcessType.MaxDescriptionLength);
            step.Property(s => s.Owner).HasConversion<string>().HasMaxLength(20);
            step.Property(s => s.AgencyName).HasMaxLength(ProcessType.MaxNameLength);
            step.Property(s => s.CustomerChecklist).HasMaxLength(ProcessType.MaxChecklistLength);
            step.Property(s => s.DeliveryChecklist).HasMaxLength(ProcessType.MaxChecklistLength);
            // Small lists that always travel with the step: stored as JSON text on the step row.
            step.Property(s => s.Documents).HasConversion(Json.Converter<IReadOnlyList<DocumentRequirement>>(), Json.Comparer<DocumentRequirement>());
            step.Property(s => s.Deliverables).HasConversion(Json.Converter<IReadOnlyList<string>>(), Json.Comparer<string>());
        });
        builder.Navigation(t => t.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
