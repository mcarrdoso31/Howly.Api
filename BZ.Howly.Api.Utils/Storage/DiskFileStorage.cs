using BZ.Howly.Api.Core.Interfaces.Storage;
using Microsoft.Extensions.Options;

namespace BZ.Howly.Api.Utils.Storage;

/// <summary>
/// Stores files on the local disk, one file per key (see <see cref="FileKeys"/>). Meant for development and tests (the
/// files can be opened right in the file explorer); several instances need <see cref="AzureBlobFileStorage"/>.
/// </summary>
public sealed class DiskFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private string Root => Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default)
    {
        var key = FileKeys.New(folder, extension);
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            // A copy cut halfway (client gone, disk full) leaves no partial file behind.
            File.Delete(path);
            throw;
        }

        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        try
        {
            return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // Missing, or deleted between two requests: the same answer, no error.
            return Task.FromResult<Stream?>(null);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        File.Delete(PathOf(key)); // does nothing when the file does not exist
        return Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        var (folder, name) = FileKeys.Parse(key);
        return Path.Combine([Root, .. folder.Split('/'), name]);
    }
}
