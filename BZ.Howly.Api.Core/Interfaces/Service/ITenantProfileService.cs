using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Interfaces.Service;

/// <summary>The data and logo of the consultancy of the current portal.</summary>
public interface ITenantProfileService
{
    public const long MaxLogoBytes = 2 * 1024 * 1024;

    Task<TenantProfileResponse> GetAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<TenantProfileResponse>> UpdateAsync(UpdateTenantProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>PNG, JPEG or WebP (checked by content), up to <see cref="MaxLogoBytes"/>. Replaces the previous logo.</summary>
    Task<OperationResult<TenantProfileResponse>> SetLogoAsync(Stream content, CancellationToken cancellationToken = default);

    Task<TenantProfileResponse> RemoveLogoAsync(CancellationToken cancellationToken = default);

    /// <summary>The logo of the current portal; null without one.</summary>
    Task<AvatarFile?> OpenLogoAsync(CancellationToken cancellationToken = default);
}
