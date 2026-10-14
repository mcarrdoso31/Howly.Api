using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>Self-service for the signed-in customer (as opposed to ICustomerService, used by the staff).</summary>
public interface ICustomerProfileService
{
    Task<OperationResult<CustomerProfileResponse>> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Saves the reviewed contact and releases the portal; the next token no longer asks for confirmation.</summary>
    Task<OperationResult<CustomerProfileResponse>> ConfirmAsync(Guid userId, ContactsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Replaces the contact list after the first access. The staff is told about the change.</summary>
    Task<OperationResult<CustomerProfileResponse>> UpdateContactsAsync(Guid userId, ContactsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets or clears the trade name, which becomes the name shown in the portal.</summary>
    Task<OperationResult<CustomerProfileResponse>> UpdateTradeNameAsync(Guid userId, UpdateTradeNameRequest request, CancellationToken cancellationToken = default);
}
