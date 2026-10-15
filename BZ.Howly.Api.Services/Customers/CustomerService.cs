using BZ.Howly.Api.Core.Customers;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Shared;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Customers;

public sealed class CustomerService(
    ICustomerRepository customerRepository,
    IUserAccountStore userAccountStore,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordGenerator passwordGenerator,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<CustomerService> logger) : ICustomerService
{
    public const int MaxPageSize = 100;

    public async Task<OperationResult<CustomerCreatedResponse>> CreateAsync(
        CreateCustomerRequest request, Guid createdBy, CancellationToken cancellationToken = default)
    {
        var contacts = ContactRequest.ToDetails(request.Contacts);
        var errors = new Dictionary<string, string[]>(Customer.Validate(request.CompanyName, contacts));
        if (!TaxId.TryParse(request.Document, out var document))
            errors["document"] = ["Enter a valid CPF or CNPJ."];
        if (errors.Count > 0)
            return OperationResult<CustomerCreatedResponse>.Invalid(errors);

        if (await customerRepository.ExistsAsync(document!, cancellationToken)
            || await userAccountStore.FindByUsernameAsync(document!.Value, cancellationToken) is not null)
            return OperationResult<CustomerCreatedResponse>.Conflict("A customer with this CPF or CNPJ already exists.");

        var temporaryPassword = passwordGenerator.Generate();

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var created = await userAccountStore.CreateAsync(
                new NewUserAccount(document!.Value, request.CompanyName.Trim(), Roles.Customer, MustConfirmProfile: true), temporaryPassword, ct);
            if (created.UsernameTaken)
                return OperationResult<CustomerCreatedResponse>.Conflict("A customer with this CPF or CNPJ already exists.");
            if (!created.Succeeded)
                throw new InvalidOperationException($"Could not create customer login: {string.Join("; ", created.Errors)}");

            var customer = Customer.Create(document, request.CompanyName, contacts, created.Account!.Id, createdBy, timeProvider.GetUtcNow());
            await customerRepository.AddAsync(customer, ct);
            await customerRepository.SaveChangesAsync(ct);

            logger.LogInformation("Customer {CustomerId} created by {UserId}.", customer.Id, createdBy);

            return OperationResult<CustomerCreatedResponse>.Success(new CustomerCreatedResponse(
                customer.Id, customer.Document.Value, customer.CompanyName, ContactResponse.From(customer.Contacts), temporaryPassword));
        }, cancellationToken);
    }

    public async Task<OperationResult<CustomerResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByIdAsync(id, cancellationToken);
        if (customer is null)
            return OperationResult<CustomerResponse>.NotFound();

        return OperationResult<CustomerResponse>.Success(await ToResponseAsync(customer, cancellationToken));
    }

    public async Task<PagedResponse<CustomerListItemResponse>> ListAsync(
        string? search, CustomerStatusFilter status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (items, total) = await customerRepository.SearchAsync(search, status, (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResponse<CustomerListItemResponse>(
            items.Select(i => new CustomerListItemResponse(i.Id, i.Document, i.CompanyName, i.IsActive, i.MustChangePassword, i.ConfirmedAt)).ToList(),
            page, pageSize, total);
    }

    public async Task<OperationResult<CustomerResponse>> UpdateAsync(
        Guid id, UpdateCustomerRequest request, Guid updatedBy, CancellationToken cancellationToken = default)
    {
        var contacts = ContactRequest.ToDetails(request.Contacts);
        var errors = Customer.Validate(request.CompanyName, contacts);
        if (errors.Count > 0)
            return OperationResult<CustomerResponse>.Invalid(errors);

        var customer = await customerRepository.FindByIdAsync(id, cancellationToken);
        if (customer is null)
            return OperationResult<CustomerResponse>.NotFound();

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            customer.Update(request.CompanyName, contacts);
            await customerRepository.SaveChangesAsync(ct);
            // The login shows the customer's name after sign in, so it follows the rename (unless a trade name is set).
            await userAccountStore.UpdateDisplayNameAsync(customer.UserId, customer.DisplayName, ct);
            return true;
        }, cancellationToken);

        logger.LogInformation("Customer {CustomerId} updated by {UserId}.", customer.Id, updatedBy);

        return OperationResult<CustomerResponse>.Success(await ToResponseAsync(customer, cancellationToken));
    }

    public async Task<OperationResult<CustomerResponse>> DeactivateAsync(Guid id, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByIdAsync(id, cancellationToken);
        if (customer is null || !await userAccountStore.SetActiveAsync(customer.UserId, false, cancellationToken))
            return OperationResult<CustomerResponse>.NotFound();

        await refreshTokenRepository.RevokeAllForUserAsync(customer.UserId, timeProvider.GetUtcNow(), cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Customer {CustomerId} deactivated by {UserId}.", customer.Id, requestedBy);

        return OperationResult<CustomerResponse>.Success(await ToResponseAsync(customer, cancellationToken));
    }

    public async Task<OperationResult<CustomerResponse>> ReactivateAsync(Guid id, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByIdAsync(id, cancellationToken);
        if (customer is null || !await userAccountStore.SetActiveAsync(customer.UserId, true, cancellationToken))
            return OperationResult<CustomerResponse>.NotFound();

        logger.LogInformation("Customer {CustomerId} reactivated by {UserId}.", customer.Id, requestedBy);

        return OperationResult<CustomerResponse>.Success(await ToResponseAsync(customer, cancellationToken));
    }

    public async Task<OperationResult<TemporaryPasswordResponse>> ResetPasswordAsync(
        Guid id, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        var customer = await customerRepository.FindByIdAsync(id, cancellationToken);
        if (customer is null)
            return OperationResult<TemporaryPasswordResponse>.NotFound();

        var temporaryPassword = passwordGenerator.Generate();
        if (!await userAccountStore.ResetPasswordAsync(customer.UserId, temporaryPassword, cancellationToken))
            return OperationResult<TemporaryPasswordResponse>.NotFound();

        await refreshTokenRepository.RevokeAllForUserAsync(customer.UserId, timeProvider.GetUtcNow(), cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Password of customer {CustomerId} reset by {UserId}.", customer.Id, requestedBy);

        return OperationResult<TemporaryPasswordResponse>.Success(new TemporaryPasswordResponse(temporaryPassword));
    }

    private async Task<CustomerResponse> ToResponseAsync(Customer customer, CancellationToken cancellationToken)
    {
        var user = await userAccountStore.FindByIdAsync(customer.UserId, cancellationToken);

        return new CustomerResponse(
            customer.Id, customer.Document.Value, customer.CompanyName, customer.TradeName, ContactResponse.From(customer.Contacts),
            user?.IsActive ?? false, user?.MustChangePassword ?? false, customer.ConfirmedAt, customer.CreatedAt);
    }
}
