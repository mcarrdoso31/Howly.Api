using BZ.Howly.Api.DAO.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

public sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        builder.ToTable("JobRuns");
        builder.HasKey(r => r.Name);
        builder.Property(r => r.Name).HasMaxLength(100).IsUnicode(false);
    }
}
