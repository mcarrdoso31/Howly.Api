using BZ.Howly.Api.Core.Customers;

namespace BZ.Howly.Api.Core.DTO.Request;

/// <param name="Phone">Brazilian number with area code; the mask is optional.</param>
/// <param name="ReceivesNotices">Whether this person receives the portal notices by email.</param>
public sealed record ContactRequest(string? Name, string? Email, string? Phone, bool ReceivesNotices)
{
    public ContactDetails ToDetails() => new(Name, Email, Phone, ReceivesNotices);

    public static IReadOnlyList<ContactDetails> ToDetails(IReadOnlyList<ContactRequest>? contacts) =>
        contacts?.Select(c => c.ToDetails()).ToList() ?? [];
}
