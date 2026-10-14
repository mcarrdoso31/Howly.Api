using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Customers;

/// <summary>A company served by the portal. Each customer has exactly one login user, identified by the CNPJ.</summary>
public sealed class Customer
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = EmailAddress.MaxLength;
    public const int MaxContacts = 10;

    private readonly List<CustomerContact> _contacts = [];

    public Guid Id { get; private set; }
    public Cnpj Cnpj { get; private set; } = null!;
    public string CompanyName { get; private set; } = string.Empty;

    /// <summary>Name the company goes by, set by the customer. Optional.</summary>
    public string? TradeName { get; private set; }

    /// <summary>Name shown in the portal: the trade name when there is one, otherwise the company name.</summary>
    public string DisplayName => TradeName ?? CompanyName;
    public IReadOnlyList<CustomerContact> Contacts => _contacts.OrderBy(c => c.Position).ToList();
    public Guid UserId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the customer reviewed and confirmed their data on first access; null until then.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    private Customer() { }

    /// <summary>
    /// Field errors keyed like the request body: "companyName", "contacts" (the list as a whole)
    /// and "contacts[i].name" / ".email" / ".phone" for each contact.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Validate(string? companyName, IReadOnlyList<ContactDetails>? contacts)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(companyName) || companyName.Trim().Length > MaxNameLength)
            errors["companyName"] = [$"Company name is required and must have at most {MaxNameLength} characters."];

        contacts ??= [];
        if (contacts.Count is 0 or > MaxContacts)
            errors["contacts"] = [$"Between 1 and {MaxContacts} contacts are required."];
        else if (!contacts.Any(c => c.ReceivesNotices))
            errors["contacts"] = ["At least one contact must receive the notices."];

        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < contacts.Count; i++)
        {
            var contact = contacts[i];
            var prefix = $"contacts[{i}]";

            if (string.IsNullOrWhiteSpace(contact.Name) || contact.Name.Trim().Length > MaxNameLength)
                errors[$"{prefix}.name"] = [$"Name is required and must have at most {MaxNameLength} characters."];

            if (!EmailAddress.IsValid(contact.Email))
                errors[$"{prefix}.email"] = ["Email must be a valid email address."];
            else if (!emails.Add(contact.Email!.Trim()))
                errors[$"{prefix}.email"] = ["Each contact must have a different email."];

            if (!BrazilianPhone.TryNormalize(contact.Phone, out _))
                errors[$"{prefix}.phone"] = ["Phone must be a Brazilian number with area code."];
        }

        return errors;
    }

    public static Customer Create(
        Cnpj cnpj, string companyName, IReadOnlyList<ContactDetails> contacts, Guid userId, Guid createdBy, DateTimeOffset now)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Cnpj = cnpj,
            UserId = userId,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
        customer.Update(companyName, contacts);
        return customer;
    }

    /// <summary>Replaces the company name and the whole contact list. The CNPJ never changes: it is the customer's login.</summary>
    public void Update(string companyName, IReadOnlyList<ContactDetails> contacts)
    {
        var errors = Validate(companyName, contacts);
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid customer: {string.Join(", ", errors.Keys)}.");

        CompanyName = companyName.Trim();
        _contacts.Clear();
        for (var i = 0; i < contacts.Count; i++)
        {
            BrazilianPhone.TryNormalize(contacts[i].Phone, out var phone);
            _contacts.Add(CustomerContact.FromValidated(contacts[i], phone, i));
        }
    }

    /// <summary>
    /// The customer reviewed their data on first access and confirmed it, possibly fixing the contacts.
    /// Confirming again only updates the contacts: the first confirmation time is kept.
    /// </summary>
    public void Confirm(IReadOnlyList<ContactDetails> contacts, DateTimeOffset now)
    {
        Update(CompanyName, contacts);
        ConfirmedAt ??= now;
    }

    /// <summary>Sets or clears (null or blank) the trade name.</summary>
    public void SetTradeName(string? tradeName)
    {
        var trimmed = string.IsNullOrWhiteSpace(tradeName) ? null : tradeName.Trim();
        if (trimmed?.Length > MaxNameLength)
            throw new ArgumentException($"Trade name must have at most {MaxNameLength} characters.", nameof(tradeName));

        TradeName = trimmed;
    }
}
