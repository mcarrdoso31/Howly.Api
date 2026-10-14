using BZ.Howly.Api.Core.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

/// <summary>Seeds the fixed roles through migrations. Ids and stamps are constant so migrations stay stable.</summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.HasData(
            Role("6f0b1a52-0c1e-4d0a-9a3e-1b0c8f6a0001", Roles.Admin),
            Role("6f0b1a52-0c1e-4d0a-9a3e-1b0c8f6a0002", Roles.Analyst),
            Role("6f0b1a52-0c1e-4d0a-9a3e-1b0c8f6a0003", Roles.Customer));
    }

    private static IdentityRole<Guid> Role(string id, string name) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = id,
    };
}
