using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Customers;

/// <summary>Contact data as typed by a person, before validation (phone may carry the mask).</summary>
public sealed record ContactDetails(string? Name, string? Email, string? Phone, bool ReceivesNotices);

/// <summary>A person of the customer company. Owned by <see cref="Customer"/>, always valid once created.</summary>
public sealed class CustomerContact
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

    /// <summary>Brazilian number, digits only (10 or 11).</summary>
    public string Phone { get; private set; } = string.Empty;

    /// <summary>Whether this person receives the portal notices by email.</summary>
    public bool ReceivesNotices { get; private set; }

    /// <summary>Order in the list as the person typed it (the database does not keep insertion order).</summary>
    public int Position { get; private set; }

    private CustomerContact() { }

    /// <summary>Builds a contact from data already checked by <see cref="Customer.Validate"/>.</summary>
    internal static CustomerContact FromValidated(ContactDetails details, string phoneDigits, int position) => new()
    {
        Id = Ids.New(),
        Name = details.Name!.Trim(),
        Email = details.Email!.Trim(),
        Phone = phoneDigits,
        ReceivesNotices = details.ReceivesNotices,
        Position = position,
    };
}
