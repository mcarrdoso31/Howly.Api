using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.DAO.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BZ.Howly.Api.DAO.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Cnpj)
            .HasConversion(cnpj => cnpj.Value, value => Cnpj.Parse(value))
            .HasMaxLength(14)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.HasIndex(c => c.Cnpj).IsUnique();

        builder.Property(c => c.CompanyName).HasMaxLength(Customer.MaxNameLength).IsRequired();
        builder.Property(c => c.TradeName).HasMaxLength(Customer.MaxNameLength);
        builder.Ignore(c => c.DisplayName);
        builder.OwnsMany(c => c.Contacts, contact =>
        {
            contact.ToTable("CustomerContacts");
            contact.WithOwner().HasForeignKey("CustomerId");
            contact.HasKey(c => c.Id);
            contact.Property(c => c.Id).ValueGeneratedNever();
            contact.Property(c => c.Name).HasMaxLength(Customer.MaxNameLength).IsRequired();
            contact.Property(c => c.Email).HasMaxLength(Customer.MaxEmailLength).IsRequired();
            contact.Property(c => c.Phone).HasMaxLength(11).IsUnicode(false).IsRequired();
        });
        builder.Navigation(c => c.Contacts).HasField("_contacts").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<AppUser>()
            .WithOne()
            .HasForeignKey<Customer>(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
