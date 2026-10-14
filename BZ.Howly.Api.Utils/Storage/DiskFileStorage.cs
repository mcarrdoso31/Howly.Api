using System.Text.RegularExpressions;
using BZ.Howly.Api.Core.Interfaces.Storage;
using Microsoft.Extensions.Options;

namespace BZ.Howly.Api.Utils.Storage;

/// <summary>
/// Stores files on the local disk as {folder}/{guid}{extension}. Keys are checked against that exact shape,
/// so a key can never point outside the root folder.
/// </summary>
public sealed partial class DiskFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex FolderPattern();

    [GeneratedRegex(@"^(?<folder>[a-z0-9-]{1,40})/(?<name>[0-9a-f]{32}\.[a-z0-9]{1,5})$")]
    private static partial Regex KeyPattern();

    /// <summary>Only the kinds of files the portal accepts; anything else is a programming error.</summary>
    private static readonly HashSet<string> AllowedExtensions = [".png", ".jpg", ".webp", ".pdf", ".bin"];

    private string Root => Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default)
    {
        if (!FolderPattern().IsMatch(folder))
            throw new ArgumentException("Invalid folder name.", nameof(folder));
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("Unsupported extension.", nameof(extension));

        var key = $"{folder}/{Guid.NewGuid():N}{extension}";
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        var match = KeyPattern().Match(key);
        if (!match.Success)
            throw new ArgumentException("Invalid file key.", nameof(key));

        return Path.Combine(Root, match.Groups["folder"].Value, match.Groups["name"].Value);
    }
}
