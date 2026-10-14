using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using BZ.Howly.Api.Core.Interfaces.Storage;
using Microsoft.Extensions.Options;

namespace BZ.Howly.Api.Utils.Storage;

/// <summary>
/// Stores files in Azure Blob Storage, one blob per key ({folder}/{guid}{extension}): every instance of the API sees the
/// same files. Same key rules as <see cref="DiskFileStorage"/>, so switching providers is configuration only.
/// </summary>
public sealed partial class AzureBlobFileStorage : IFileStorage
{
    [GeneratedRegex("^[a-z0-9-]{1,40}(?:/[a-z0-9-]{1,40}){0,2}$")]
    private static partial Regex FolderPattern();

    [GeneratedRegex(@"^[a-z0-9-]{1,40}(?:/[a-z0-9-]{1,40}){0,2}/[0-9a-f]{32}\.[a-z0-9]{1,5}$")]
    private static partial Regex KeyPattern();

    private readonly BlobContainerClient _container;
    private readonly Lazy<Task> _created;

    public AzureBlobFileStorage(IOptions<FileStorageOptions> options)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
            throw new InvalidOperationException("FileStorage:ConnectionString is required for the AzureBlob provider.");

        _container = new BlobContainerClient(settings.ConnectionString, settings.Container);
        _created = new Lazy<Task>(() => _container.CreateIfNotExistsAsync());
    }

    public async Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default)
    {
        var key = FileKeys.New(folder, extension, FolderPattern());
        await _created.Value;
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

    private BlobClient Blob(string key) =>
        KeyPattern().IsMatch(key) ? _container.GetBlobClient(key) : throw new ArgumentException("Invalid file key.", nameof(key));
}
