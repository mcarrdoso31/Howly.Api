using BZ.Howly.Api.Core.Customers;

namespace BZ.Howly.Api.Core.DTO.Response;

/// <param name="Phone">Digits only; clients apply the mask.</param>
public sealed record ContactResponse(Guid Id, string Name, string Email, string Phone, bool ReceivesNotices)
{
    public static IReadOnlyList<ContactResponse> From(IEnumerable<CustomerContact> contacts) =>
        contacts.Select(c => new ContactResponse(c.Id, c.Name, c.Email, c.Phone, c.ReceivesNotices)).ToList();
}
