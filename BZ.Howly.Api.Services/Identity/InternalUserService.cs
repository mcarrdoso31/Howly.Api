using System.Text.RegularExpressions;
using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Core.Shared.Exceptions;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Identity;

public sealed partial class InternalUserService(
    IUserAccountStore userAccountStore,
    IRefreshTokenRepository refreshTokenRepository,
    IProcessRepository processRepository,
    INotificationService notificationService,
    IPasswordGenerator passwordGenerator,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<InternalUserService> logger) : IInternalUserService
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxPageSize = 100;

    [GeneratedRegex("^[a-z0-9._-]{3,50}$")]
    private static partial Regex UsernamePattern();

    public async Task<OperationResult<InternalUserCreatedResponse>> CreateAsync(
        CreateInternalUserRequest request, CancellationToken cancellationToken = default)
    {
        var username = request.Username?.Trim().ToLowerInvariant() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;

        var errors = Validate(username, displayName, request.Role);
        if (errors.Count > 0)
            return OperationResult<InternalUserCreatedResponse>.Invalid(errors);

        var temporaryPassword = passwordGenerator.Generate();

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var created = await userAccountStore.CreateAsync(new NewUserAccount(username, displayName, request.Role), temporaryPassword, ct);
            if (created.UsernameTaken)
                return OperationResult<InternalUserCreatedResponse>.Conflict("Username is already in use.");
            if (!created.Succeeded)
                throw new InvalidOperationException($"Could not create internal user: {string.Join("; ", created.Errors)}");

            logger.LogInformation("Internal user {UserId} ({Role}) created.", created.Account!.Id, request.Role);

            return OperationResult<InternalUserCreatedResponse>.Success(
                new InternalUserCreatedResponse(created.Account.Id, username, displayName, request.Role, temporaryPassword));
        }, cancellationToken);
    }

    public async Task<PagedResponse<InternalUserListItemResponse>> ListAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (items, total) = await userAccountStore.SearchAsync(Roles.Internal, search, (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResponse<InternalUserListItemResponse>(
            items.Select(u => new InternalUserListItemResponse(u.Id, u.Username, u.DisplayName, u.Roles, u.MustChangePassword, u.IsActive)).ToList(),
            page, pageSize, total);
    }

    public async Task<IReadOnlyList<PersonResponse>> ListActiveStaffAsync(CancellationToken cancellationToken = default) =>
        (await userAccountStore.ListActiveAsync(Roles.Internal, cancellationToken))
            .Select(a => new PersonResponse(a.Id, a.DisplayName, a.AvatarVersion))
            .ToList();

    public async Task<OperationResult<InternalUserResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await FindInternalAsync(id, cancellationToken);
        return user is null
            ? OperationResult<InternalUserResponse>.NotFound()
            : OperationResult<InternalUserResponse>.Success(await ToResponseAsync(user, cancellationToken));
    }

    public async Task<OperationResult<InternalUserResponse>> UpdateAsync(
        Guid id, UpdateInternalUserRequest request, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        var user = await FindInternalAsync(id, cancellationToken);
        if (user is null)
            return OperationResult<InternalUserResponse>.NotFound();

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (displayName.Length is 0 or > MaxDisplayNameLength)
            errors["displayName"] = [$"Display name is required and must have at most {MaxDisplayNameLength} characters."];
        if (request.Role is null || !Roles.Internal.Contains(request.Role))
            errors["role"] = [$"Role must be one of: {string.Join(", ", Roles.Internal)}."];
        if (errors.Count > 0)
            return OperationResult<InternalUserResponse>.Invalid(errors);

        if (user.Roles.Contains(Roles.Admin) && request.Role != Roles.Admin && user.IsActive
            && await userAccountStore.CountActiveInRoleAsync(Roles.Admin, cancellationToken) <= 1)
            return OperationResult<InternalUserResponse>.Conflict("The last active Admin cannot stop being Admin.");

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await userAccountStore.UpdateDisplayNameAsync(id, displayName, ct);
            await userAccountStore.SetInternalRoleAsync(id, request.Role!, ct);
            return true;
        }, cancellationToken);

        logger.LogInformation("Internal user {UserId} updated by {RequestedBy} (role {Role}).", id, requestedBy, request.Role);

        return OperationResult<InternalUserResponse>.Success(await ToResponseAsync((await FindInternalAsync(id, cancellationToken))!, cancellationToken));
    }

    public async Task<OperationResult<InternalUserResponse>> DeactivateAsync(
        Guid id, DeactivateInternalUserRequest request, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        var user = await FindInternalAsync(id, cancellationToken);
        if (user is null)
            return OperationResult<InternalUserResponse>.NotFound();
        if (id == requestedBy)
            return OperationResult<InternalUserResponse>.Conflict("Nobody can deactivate themselves.");
        if (user.Roles.Contains(Roles.Admin) && user.IsActive && await userAccountStore.CountActiveInRoleAsync(Roles.Admin, cancellationToken) <= 1)
            return OperationResult<InternalUserResponse>.Conflict("The last active Admin cannot be deactivated.");

        var open = await processRepository.ListOpenAssignedToAsync(id, cancellationToken);
        UserAccount? heir = null;
        if (open.Count > 0)
        {
            heir = request.ReassignTo is { } heirId && heirId != id ? await FindInternalAsync(heirId, cancellationToken) : null;
            if (heir is not { IsActive: true })
                return OperationResult<InternalUserResponse>.Invalid("reassignTo",
                    "This person still has open processes: choose another active staff member to take them over.");
        }

        var admin = await userAccountStore.FindByIdAsync(requestedBy, cancellationToken)
            ?? throw new UnauthorizedException("The signed-in user no longer exists.");
        var now = timeProvider.GetUtcNow();

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var process in open)
            {
                var eventsBefore = process.Events.Count;
                process.Assign(heir!.Id, heir.DisplayName, new Actor(admin.Id, admin.DisplayName), now);
                await notificationService.NotifyAsync(process, eventsBefore, requestedBy, ct);
            }
            await processRepository.SaveChangesAsync(ct);

            await userAccountStore.SetActiveAsync(id, false, ct);
            await refreshTokenRepository.RevokeAllForUserAsync(id, now, ct);
            await refreshTokenRepository.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);

        logger.LogInformation("Internal user {UserId} deactivated by {RequestedBy}; {Count} open processes handed to {HeirId}.",
            id, requestedBy, open.Count, heir?.Id);

        return OperationResult<InternalUserResponse>.Success(await ToResponseAsync((await FindInternalAsync(id, cancellationToken))!, cancellationToken));
    }

    public async Task<OperationResult<InternalUserResponse>> ReactivateAsync(Guid id, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        if (await FindInternalAsync(id, cancellationToken) is null)
            return OperationResult<InternalUserResponse>.NotFound();

        await userAccountStore.SetActiveAsync(id, true, cancellationToken);
        logger.LogInformation("Internal user {UserId} reactivated by {RequestedBy}.", id, requestedBy);

        return OperationResult<InternalUserResponse>.Success(await ToResponseAsync((await FindInternalAsync(id, cancellationToken))!, cancellationToken));
    }

    private async Task<InternalUserResponse> ToResponseAsync(UserAccount user, CancellationToken cancellationToken) =>
        new(user.Id, user.Username, user.DisplayName, user.Roles, user.MustChangePassword, user.IsActive,
            (await processRepository.ListOpenAssignedToAsync(user.Id, cancellationToken)).Count);

    public async Task<OperationResult<TemporaryPasswordResponse>> ResetPasswordAsync(
        Guid id, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        if (await FindInternalAsync(id, cancellationToken) is null)
            return OperationResult<TemporaryPasswordResponse>.NotFound();

        var temporaryPassword = passwordGenerator.Generate();
        await userAccountStore.ResetPasswordAsync(id, temporaryPassword, cancellationToken);
        await refreshTokenRepository.RevokeAllForUserAsync(id, timeProvider.GetUtcNow(), cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Password of internal user {UserId} reset by {RequestedBy}.", id, requestedBy);

        return OperationResult<TemporaryPasswordResponse>.Success(new TemporaryPasswordResponse(temporaryPassword));
    }

    private async Task<UserAccount?> FindInternalAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await userAccountStore.FindByIdAsync(id, cancellationToken);
        return user is not null && user.HasAnyRole(Roles.Internal) ? user : null;
    }

    private static Dictionary<string, string[]> Validate(string username, string displayName, string? role)
    {
        var errors = new Dictionary<string, string[]>();

        if (!UsernamePattern().IsMatch(username))
            errors["username"] = ["Username must have 3 to 50 characters: lowercase letters, digits, '.', '_' or '-'."];
        else if (TaxId.TryParse(username, out _))
            errors["username"] = ["Username cannot be a CPF or CNPJ; they are reserved for customer logins."];

        if (displayName.Length is 0 or > MaxDisplayNameLength)
            errors["displayName"] = [$"Display name is required and must have at most {MaxDisplayNameLength} characters."];

        if (role is null || !Roles.Internal.Contains(role))
            errors["role"] = [$"Role must be one of: {string.Join(", ", Roles.Internal)}."];

        return errors;
    }
}
