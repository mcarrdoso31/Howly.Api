using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

public sealed class DocumentExampleConfiguration : IEntityTypeConfiguration<DocumentExample>
{
    public void Configure(EntityTypeBuilder<DocumentExample> builder)
    {
        builder.ToTable("DocumentExamples");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.FileKey).HasMaxLength(150).IsUnicode(false).IsRequired();
        builder.Property(e => e.FileName).HasMaxLength(UploadPolicy.MaxFileNameLength).IsRequired();
    }
}
