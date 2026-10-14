using System.Text.RegularExpressions;

namespace BZ.Howly.Api.Utils.Storage;

/// <summary>The keys every storage generates: {folder}/{guid}{extension}, from a short list of extensions.</summary>
internal static class FileKeys
{
    /// <summary>Only the kinds of files the portal accepts; anything else is a programming error.</summary>
    private static readonly HashSet<string> AllowedExtensions = [".png", ".jpg", ".webp", ".pdf", ".bin"];

    public static string New(string folder, string extension, Regex folderPattern)
    {
        if (!folderPattern.IsMatch(folder))
            throw new ArgumentException("Invalid folder name.", nameof(folder));
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("Unsupported extension.", nameof(extension));

        return $"{folder}/{Guid.NewGuid():N}{extension}";
    }
}
