using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

public interface ICustomerService
{
    Task<OperationResult<CustomerCreatedResponse>> CreateAsync(CreateCustomerRequest request, Guid createdBy, CancellationToken cancellationToken = default);
    Task<OperationResult<CustomerResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResponse<CustomerListItemResponse>> ListAsync(
        string? search, CustomerStatusFilter status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<OperationResult<CustomerResponse>> UpdateAsync(Guid id, UpdateCustomerRequest request, Guid updatedBy, CancellationToken cancellationToken = default);

    /// <summary>Blocks sign in and ends every open session. Calling it again changes nothing.</summary>
    Task<OperationResult<CustomerResponse>> DeactivateAsync(Guid id, Guid requestedBy, CancellationToken cancellationToken = default);
    Task<OperationResult<CustomerResponse>> ReactivateAsync(Guid id, Guid requestedBy, CancellationToken cancellationToken = default);
    Task<OperationResult<TemporaryPasswordResponse>> ResetPasswordAsync(Guid id, Guid requestedBy, CancellationToken cancellationToken = default);
}
