using System.Text.RegularExpressions;
using BZ.Howly.Api.Core.Interfaces.Storage;
using Microsoft.Extensions.Options;

namespace BZ.Howly.Api.Utils.Storage;

/// <summary>
/// Stores files on the local disk as {folder}/{guid}{extension}, the folder being up to three levels deep
/// (e.g. {tenant}/documents). Keys are checked against that exact shape, so a key can never point outside the root.
/// Meant for development and tests (the files can be opened right in the file explorer); several instances need
/// <see cref="AzureBlobFileStorage"/>.
/// </summary>
public sealed partial class DiskFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    [GeneratedRegex("^[a-z0-9-]{1,40}(?:/[a-z0-9-]{1,40}){0,2}$")]
    private static partial Regex FolderPattern();

    [GeneratedRegex(@"^(?<folder>[a-z0-9-]{1,40}(?:/[a-z0-9-]{1,40}){0,2})/(?<name>[0-9a-f]{32}\.[a-z0-9]{1,5})$")]
    private static partial Regex KeyPattern();

    private string Root => Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default)
    {
        var key = FileKeys.New(folder, extension, FolderPattern());
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
        var match = KeyPattern().Match(key);
        if (!match.Success)
            throw new ArgumentException("Invalid file key.", nameof(key));

        return Path.Combine([Root, .. match.Groups["folder"].Value.Split('/'), match.Groups["name"].Value]);
    }
}
