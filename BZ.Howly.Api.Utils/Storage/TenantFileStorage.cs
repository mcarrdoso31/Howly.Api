using BZ.Howly.Api.Core.Interfaces.Storage;
using BZ.Howly.Api.Core.Tenancy;

namespace BZ.Howly.Api.Utils.Storage;

/// <summary>
/// Keeps every consultancy's files apart, whatever the provider: new files go under {tenant}/{folder}, and a key of
/// another consultancy is refused even if it leaked somehow.
/// </summary>
public sealed class TenantFileStorage(IFileStorage inner, ITenantContext tenantContext) : IFileStorage
{
    public Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default) =>
        inner.SaveAsync($"{Prefix}/{folder}", extension, content, cancellationToken);

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        inner.OpenReadAsync(Own(key), cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(Own(key), cancellationToken);

    private string Prefix => (tenantContext.TenantId ?? throw new InvalidOperationException("Files belong to a consultancy: no tenant set.")).ToString("N");

    private string Own(string key) =>
        key.StartsWith(Prefix + "/", StringComparison.Ordinal) ? key : throw new ArgumentException("The file belongs to another consultancy.", nameof(key));
}
