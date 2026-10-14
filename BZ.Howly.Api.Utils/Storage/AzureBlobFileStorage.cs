using Azure;
using Azure.Storage.Blobs;
using BZ.Howly.Api.Core.Interfaces.Storage;
using Microsoft.Extensions.Options;

namespace BZ.Howly.Api.Utils.Storage;

/// <summary>
/// Stores files in Azure Blob Storage, one blob per key (see <see cref="FileKeys"/>): every instance of the API sees the
/// same files. Same keys as <see cref="DiskFileStorage"/>, so switching providers is configuration only.
/// </summary>
public sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _creating = new(1, 1);
    private volatile bool _created;

    public AzureBlobFileStorage(IOptions<FileStorageOptions> options)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
            throw new InvalidOperationException("FileStorage:ConnectionString is required for the AzureBlob provider.");

        _container = new BlobContainerClient(settings.ConnectionString, settings.Container);
    }

    public async Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default)
    {
        var key = FileKeys.New(folder, extension);
        await EnsureContainerAsync(cancellationToken);
        // A failed upload leaves nothing behind: the blob only exists once it is complete.
        await _container.GetBlobClient(key).UploadAsync(content, overwrite: false, cancellationToken);
        return key;
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Blob(key).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await Blob(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    private BlobClient Blob(string key)
    {
        FileKeys.Parse(key); // refuses keys this storage did not generate
        return _container.GetBlobClient(key);
    }

    /// <summary>Creates the container once; a failure (storage briefly down) is not remembered, the next upload tries again.</summary>
    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_created)
            return;

        await _creating.WaitAsync(cancellationToken);
        try
        {
            if (!_created)
            {
                await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                _created = true;
            }
        }
        finally
        {
            _creating.Release();
        }
    }
}
