using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>The signed-in person's own account: profile data and photo (for customers, the company logo).</summary>
public interface IAccountProfileService
{
    public const long MaxAvatarBytes = 2 * 1024 * 1024;

    Task<OperationResult<AccountProfileResponse>> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<OperationResult<AccountProfileResponse>> UpdateStaffAsync(Guid userId, UpdateStaffProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>PNG, JPEG or WebP (checked by content), up to <see cref="MaxAvatarBytes"/>. Replaces the previous image.</summary>
    Task<OperationResult<AccountProfileResponse>> SetAvatarAsync(Guid userId, Stream content, CancellationToken cancellationToken = default);
    Task<OperationResult<AccountProfileResponse>> RemoveAvatarAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The image of <paramref name="userId"/> as seen by <paramref name="viewerId"/>: staff see everyone,
    /// customers see themselves and the staff. Null when there is none or it is not visible to the viewer.
    /// </summary>
    Task<AvatarFile?> OpenAvatarAsync(Guid viewerId, Guid userId, CancellationToken cancellationToken = default);
}

public sealed record AvatarFile(Stream Content, string ContentType, string Version);
