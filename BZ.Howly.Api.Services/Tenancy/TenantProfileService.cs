using BZ.Howly.Api.Core.DTO.Request;
using BZ.Howly.Api.Core.DTO.Response;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Interfaces.Storage;
using BZ.Howly.Api.Core.Shared;
using BZ.Howly.Api.Core.Tenancy;
using BZ.Howly.Api.Services.Shared;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Tenancy;

public sealed class TenantProfileService(
    ITenantContext tenantContext,
    ITenantRepository tenantRepository,
    IFileStorage fileStorage,
    TimeProvider timeProvider,
    ILogger<TenantProfileService> logger) : ITenantProfileService
{
    private const string LogoFolder = "branding";

    public async Task<TenantProfileResponse> GetAsync(CancellationToken cancellationToken = default) =>
        TenantProfileResponse.From(await CurrentAsync(cancellationToken));

    public async Task<OperationResult<TenantProfileResponse>> UpdateAsync(UpdateTenantProfileRequest request, CancellationToken cancellationToken = default)
    {
        var (name, profile, errors) = TenantProfile.Normalize(
            request.Name, request.LegalName, request.Cnpj, request.Email, request.Phone, request.PostalCode,
            request.Street, request.City, request.State, request.TimeZone);
        if (errors.Count > 0)
            return OperationResult<TenantProfileResponse>.Invalid(errors);

        var tenant = await CurrentAsync(cancellationToken);
        tenant.UpdateProfile(name!, profile!);
        await tenantRepository.SaveChangesAsync(cancellationToken);
        return OperationResult<TenantProfileResponse>.Success(TenantProfileResponse.From(tenant));
    }

    public async Task<OperationResult<TenantProfileResponse>> SetLogoAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var (image, format, error) = await Images.ReadAsync(content, ITenantProfileService.MaxLogoBytes, cancellationToken);
        if (error is not null)
            return OperationResult<TenantProfileResponse>.Invalid("file", error);

        using (image)
        {
            var tenant = await CurrentAsync(cancellationToken);
            var key = await fileStorage.SaveAsync(LogoFolder, format!.Extension, image!, cancellationToken);
            var previous = tenant.SetLogo(key, timeProvider.GetUtcNow());
            await tenantRepository.SaveChangesAsync(cancellationToken);
            await DeleteQuietlyAsync(previous, cancellationToken);
            return OperationResult<TenantProfileResponse>.Success(TenantProfileResponse.From(tenant));
        }
    }

    public async Task<TenantProfileResponse> RemoveLogoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await CurrentAsync(cancellationToken);
        var previous = tenant.SetLogo(null, timeProvider.GetUtcNow());
        await tenantRepository.SaveChangesAsync(cancellationToken);
        await DeleteQuietlyAsync(previous, cancellationToken);
        return TenantProfileResponse.From(tenant);
    }

    public async Task<AvatarFile?> OpenLogoAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await CurrentAsync(cancellationToken);
        if (tenant.LogoKey is null || tenant.LogoVersion is null)
            return null;

        var format = ImageFormat.FromExtension(Path.GetExtension(tenant.LogoKey));
        var stream = format is null ? null : await fileStorage.OpenReadAsync(tenant.LogoKey, cancellationToken);
        return stream is null ? null : new AvatarFile(stream, format!.ContentType, tenant.LogoVersion);
    }

    /// <summary>The consultancy of the current portal, freshly read (the one in the context comes from a cache).</summary>
    private async Task<Tenant> CurrentAsync(CancellationToken cancellationToken)
    {
        var id = tenantContext.TenantId ?? throw new InvalidOperationException("No consultancy in this request.");
        return await tenantRepository.FindForUpdateAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"The consultancy {id} does not exist.");
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
            // The consultancy already points to the new logo; an orphan file is harmless.
            logger.LogWarning(exception, "Could not delete the previous logo {Key}.", key);
        }
    }
}
