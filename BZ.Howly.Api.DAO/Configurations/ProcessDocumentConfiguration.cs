using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

public sealed class ProcessDocumentConfiguration : IEntityTypeConfiguration<ProcessDocument>
{
    private const int EnumLength = 20;

    public void Configure(EntityTypeBuilder<ProcessDocument> builder)
    {
        builder.ToTable("ProcessDocuments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Name).HasMaxLength(ProcessDocument.MaxNameLength).IsRequired();
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(EnumLength);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(EnumLength);
        builder.Property(d => d.Version).IsConcurrencyToken();
        builder.Ignore(d => d.LatestVersion);
        builder.Ignore(d => d.IsPending);
        builder.HasIndex(d => new { d.ProcessId, d.StepId });
        // The documents of a process are part of its history: never deleted along with it.
        builder.HasOne<Process>().WithMany().HasForeignKey(d => d.ProcessId).OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(d => d.Versions, version =>
        {
            version.ToTable("DocumentVersions");
            version.WithOwner().HasForeignKey("DocumentId");
            version.HasKey(v => v.Id);
            version.Property(v => v.Id).ValueGeneratedNever();
            version.Property(v => v.FileKey).HasMaxLength(150).IsUnicode(false).IsRequired();
            version.Property(v => v.FileName).HasMaxLength(UploadPolicy.MaxFileNameLength).IsRequired();
            version.Property(v => v.UploadedByName).HasMaxLength(200).IsRequired();
            version.Property(v => v.Status).HasConversion<string>().HasMaxLength(EnumLength);
            version.Property(v => v.ReviewReason).HasMaxLength(Process.MaxCommentLength);
            version.Property(v => v.ReviewedByName).HasMaxLength(200);
            version.HasIndex("DocumentId", nameof(DocumentVersion.Number)).IsUnique();
        });
        builder.Navigation(d => d.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
