using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Interfaces.Storage;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Services.Shared;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Identity;

public sealed class AccountProfileService(
    IUserAccountStore userAccountStore,
    IFileStorage fileStorage,
    TimeProvider timeProvider,
    ILogger<AccountProfileService> logger) : IAccountProfileService
{
    private const string AvatarFolder = "avatars";

    public async Task<OperationResult<AccountProfileResponse>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await userAccountStore.GetProfileAsync(userId, cancellationToken);
        return profile is null ? OperationResult<AccountProfileResponse>.NotFound() : OperationResult<AccountProfileResponse>.Success(ToResponse(profile));
    }

    public async Task<OperationResult<AccountProfileResponse>> UpdateStaffAsync(
        Guid userId, UpdateStaffProfileRequest request, CancellationToken cancellationToken = default)
    {
        var changes = new StaffProfileChanges(
            request.DisplayName, request.Nickname, request.Email, request.Phone, request.SecondaryEmail, request.SecondaryPhone);

        var errors = StaffProfileChanges.Validate(changes);
        if (errors.Count > 0)
            return OperationResult<AccountProfileResponse>.Invalid(errors);

        if (!await userAccountStore.UpdateStaffProfileAsync(userId, changes.Normalize(), cancellationToken))
            return OperationResult<AccountProfileResponse>.NotFound();

        return await GetAsync(userId, cancellationToken);
    }

    public async Task<OperationResult<AccountProfileResponse>> SetAvatarAsync(Guid userId, Stream content, CancellationToken cancellationToken = default)
    {
        var (image, format, error) = await Images.ReadAsync(content, IAccountProfileService.MaxAvatarBytes, cancellationToken);
        if (error is not null)
            return OperationResult<AccountProfileResponse>.Invalid("file", error);
        using var buffer = image!;

        var profile = await userAccountStore.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
            return OperationResult<AccountProfileResponse>.NotFound();

        var key = await fileStorage.SaveAsync(AvatarFolder, format!.Extension, buffer, cancellationToken);
        await userAccountStore.SetAvatarAsync(userId, key, timeProvider.GetUtcNow(), cancellationToken);
        await DeleteQuietlyAsync(profile.AvatarKey, cancellationToken);

        return await GetAsync(userId, cancellationToken);
    }

    public async Task<OperationResult<AccountProfileResponse>> RemoveAvatarAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await userAccountStore.GetProfileAsync(userId, cancellationToken);
        if (profile is null)
            return OperationResult<AccountProfileResponse>.NotFound();

        await userAccountStore.SetAvatarAsync(userId, null, null, cancellationToken);
        await DeleteQuietlyAsync(profile.AvatarKey, cancellationToken);

        return await GetAsync(userId, cancellationToken);
    }

    public async Task<AvatarFile?> OpenAvatarAsync(Guid viewerId, Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await userAccountStore.GetProfileAsync(userId, cancellationToken);
        if (profile?.AvatarKey is null || profile.AvatarVersion is null)
            return null;

        if (viewerId != userId && profile.Roles.Contains(Roles.Customer))
        {
            // A customer's logo is visible to the staff only, never to other customers.
            var viewer = await userAccountStore.FindByIdAsync(viewerId, cancellationToken);
            if (viewer is null || !viewer.HasAnyRole(Roles.Internal))
                return null;
        }

        var format = ImageFormat.FromExtension(Path.GetExtension(profile.AvatarKey));
        var stream = format is null ? null : await fileStorage.OpenReadAsync(profile.AvatarKey, cancellationToken);
        return stream is null ? null : new AvatarFile(stream, format!.ContentType, profile.AvatarVersion);
    }

    private async Task DeleteQuietlyAsync(string? key, CancellationToken cancellationToken)
    {
        if (key is null)
            return;

        try
        {
            await fileStorage.DeleteAsync(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever the provider says (disk, blob service down…): the change is saved, so it is not an error.
            // The account already points to the new image; an orphan file is harmless.
            logger.LogWarning(exception, "Could not delete the old avatar {Key}.", key);
        }
    }

    private static AccountProfileResponse ToResponse(AccountProfile profile) => new(
        profile.UserId, profile.Username, profile.DisplayName, profile.Nickname, profile.Email, profile.Phone,
        profile.SecondaryEmail, profile.SecondaryPhone, profile.AvatarVersion);
}
