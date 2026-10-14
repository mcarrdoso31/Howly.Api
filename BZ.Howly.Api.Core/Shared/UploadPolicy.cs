namespace BZ.Howly.Api.Core.Shared;

public enum UploadCheck
{
    Accepted,

    /// <summary>The extension is of an executable, a script or active web content.</summary>
    BlockedType,

    /// <summary>The first bytes are of an executable or script, whatever the name says.</summary>
    BlockedContent,

    /// <summary>No usable file name (it needs an extension).</summary>
    MissingName,

    Empty,
}

/// <summary>
/// What may be uploaded as a process document: any ordinary document (PDF, Office, text, XML, images, archives…), never
/// executables, scripts nor content a browser would run. Files are always downloaded as attachments, so even an accepted
/// file never runs inside the portal. Malware scanning is a later step.
/// </summary>
public static class UploadPolicy
{
    public const long MaxBytes = 25 * 1024 * 1024;
    public const int MaxFileNameLength = 200;
    private const int MaxExtensionLength = 20;

    /// <summary>Bytes <see cref="Check"/> needs to look at.</summary>
    public const int HeaderLength = 8;

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows executables, installers and shortcuts
        ".exe", ".dll", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".mst", ".msc", ".lnk", ".reg", ".inf", ".gadget", ".appx", ".msix",
        // Scripts
        ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".vbs", ".vbe", ".js", ".jse", ".mjs", ".wsf", ".wsh", ".sh", ".bash", ".zsh", ".py", ".pl", ".rb",
        ".php", ".jar", ".class",
        // Content a browser would run
        ".html", ".htm", ".xhtml", ".shtml", ".svg", ".hta", ".mht", ".mhtml",
        // Other platforms' executables and disk images that carry them
        ".app", ".apk", ".dmg", ".deb", ".rpm", ".iso", ".img",
    };

    private static readonly HashSet<string> MacroExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docm", ".dotm", ".xlsm", ".xltm", ".xlam", ".pptm", ".potm", ".ppam", ".ppsm",
    };

    public static UploadCheck Check(string? fileName, ReadOnlySpan<byte> header)
    {
        var name = CleanFileName(fileName);
        var extension = Path.GetExtension(name);
        if (name.Length == 0 || extension.Length < 2)
            return UploadCheck.MissingName;
        if (BlockedExtensions.Contains(extension))
            return UploadCheck.BlockedType;
        if (header.IsEmpty)
            return UploadCheck.Empty;
        if (LooksExecutable(header))
            return UploadCheck.BlockedContent;
        return UploadCheck.Accepted;
    }

    /// <summary>Office formats that can carry macros: accepted, but flagged to whoever opens them.</summary>
    public static bool MayContainMacros(string? fileName) => MacroExtensions.Contains(Path.GetExtension(CleanFileName(fileName)));

    /// <summary>The name kept for downloads: no folders, trimmed, at most <see cref="MaxFileNameLength"/> characters (extension kept).</summary>
    public static string CleanFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (name.Length <= MaxFileNameLength)
            return name;

        // An "extension" too long to keep (a crafted name) is cut along with the rest.
        var extension = Path.GetExtension(name);
        return extension.Length > MaxExtensionLength
            ? name[..MaxFileNameLength]
            : name[..(MaxFileNameLength - extension.Length)] + extension;
    }

    private static bool LooksExecutable(ReadOnlySpan<byte> header) =>
        header.StartsWith("MZ"u8) ||                                   // Windows PE
        header.StartsWith((ReadOnlySpan<byte>)[0x7F, 0x45, 0x4C, 0x46]) || // ELF
        header.StartsWith("#!"u8) ||                                   // script with an interpreter line
        header.StartsWith((ReadOnlySpan<byte>)[0xCA, 0xFE, 0xBA, 0xBE]) || // Java class / Mach-O universal
        header.StartsWith((ReadOnlySpan<byte>)[0xCF, 0xFA, 0xED, 0xFE]);   // Mach-O 64-bit
}
