namespace BZ.Howly.Api.Core.DTO.Request;

/// <summary>The customer's contact list, sent by the customer on first access and on later edits.</summary>
public sealed record ContactsRequest(IReadOnlyList<ContactRequest>? Contacts);
