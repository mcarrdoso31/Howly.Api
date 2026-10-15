using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Shared;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Customers;

public sealed class CustomerProfileService(
    ICustomerRepository customerRepository,
    IUserAccountStore userAccountStore,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<CustomerProfileService> logger) : ICustomerProfileService
{
    public async Task<OperationResult<CustomerProfileResponse>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByUserIdAsync(userId, cancellationToken);
        return customer is null
            ? OperationResult<CustomerProfileResponse>.NotFound()
            : OperationResult<CustomerProfileResponse>.Success(ToResponse(customer));
    }

    public async Task<OperationResult<CustomerProfileResponse>> ConfirmAsync(
        Guid userId, ContactsRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByUserIdAsync(userId, cancellationToken);
        if (customer is null)
            return OperationResult<CustomerProfileResponse>.NotFound();

        var contacts = ContactRequest.ToDetails(request.Contacts);
        var errors = Customer.Validate(customer.CompanyName, contacts);
        if (errors.Count > 0)
            return OperationResult<CustomerProfileResponse>.Invalid(errors);

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            customer.Confirm(contacts, timeProvider.GetUtcNow());
            await customerRepository.SaveChangesAsync(ct);
            await userAccountStore.CompleteProfileConfirmationAsync(userId, ct);
            return true;
        }, cancellationToken);

        logger.LogInformation("Customer {CustomerId} confirmed their data.", customer.Id);

        return OperationResult<CustomerProfileResponse>.Success(ToResponse(customer));
    }

    public async Task<OperationResult<CustomerProfileResponse>> UpdateContactsAsync(
        Guid userId, ContactsRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByUserIdAsync(userId, cancellationToken);
        if (customer is null)
            return OperationResult<CustomerProfileResponse>.NotFound();

        var contacts = ContactRequest.ToDetails(request.Contacts);
        var errors = Customer.Validate(customer.CompanyName, contacts);
        if (errors.Count > 0)
            return OperationResult<CustomerProfileResponse>.Invalid(errors);

        customer.Update(customer.CompanyName, contacts);
        await customerRepository.SaveChangesAsync(cancellationToken);

        // The staff must know: for now through the log, later as a portal notification.
        logger.LogInformation("Customer {CustomerId} changed their contacts ({Count} now). Staff notice pending.", customer.Id, contacts.Count);

        return OperationResult<CustomerProfileResponse>.Success(ToResponse(customer));
    }

    public async Task<OperationResult<CustomerProfileResponse>> UpdateTradeNameAsync(
        Guid userId, UpdateTradeNameRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByUserIdAsync(userId, cancellationToken);
        if (customer is null)
            return OperationResult<CustomerProfileResponse>.NotFound();

        if (request.TradeName?.Trim().Length > Customer.MaxNameLength)
            return OperationResult<CustomerProfileResponse>.Invalid("tradeName", $"Trade name must have at most {Customer.MaxNameLength} characters.");

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            customer.SetTradeName(request.TradeName);
            await customerRepository.SaveChangesAsync(ct);
            await userAccountStore.UpdateDisplayNameAsync(userId, customer.DisplayName, ct);
            return true;
        }, cancellationToken);

        return OperationResult<CustomerProfileResponse>.Success(ToResponse(customer));
    }

    private static CustomerProfileResponse ToResponse(Customer customer) =>
        new(customer.Document.Value, customer.CompanyName, customer.TradeName, ContactResponse.From(customer.Contacts), customer.ConfirmedAt);
}
