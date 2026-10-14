using System.Text.RegularExpressions;

namespace BZ.Howly.Api.Utils.Storage;

/// <summary>
/// The keys every storage generates and accepts: {folder}/{guid}{extension}, the folder up to three levels deep
/// (e.g. {tenant}/documents), from a short list of extensions. One shape for all providers, so switching provider is
/// configuration only, and a key can never point outside the storage.
/// </summary>
internal static partial class FileKeys
{
    /// <summary>Only the kinds of files the portal accepts; anything else is a programming error.</summary>
    private static readonly HashSet<string> AllowedExtensions = [".png", ".jpg", ".webp", ".pdf", ".bin"];

    [GeneratedRegex("^[a-z0-9-]{1,40}(?:/[a-z0-9-]{1,40}){0,2}$")]
    private static partial Regex FolderPattern();

    [GeneratedRegex(@"^(?<folder>[a-z0-9-]{1,40}(?:/[a-z0-9-]{1,40}){0,2})/(?<name>[0-9a-f]{32}\.[a-z0-9]{1,5})$")]
    private static partial Regex KeyPattern();

    public static string New(string folder, string extension)
    {
        if (!FolderPattern().IsMatch(folder))
            throw new ArgumentException("Invalid folder name.", nameof(folder));
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("Unsupported extension.", nameof(extension));

        return $"{folder}/{Guid.NewGuid():N}{extension}";
    }

    /// <summary>The folder and file name of a key this storage generated; anything else is refused.</summary>
    public static (string Folder, string Name) Parse(string key)
    {
        var match = KeyPattern().Match(key);
        if (!match.Success)
            throw new ArgumentException("Invalid file key.", nameof(key));
        return (match.Groups["folder"].Value, match.Groups["name"].Value);
    }
}
