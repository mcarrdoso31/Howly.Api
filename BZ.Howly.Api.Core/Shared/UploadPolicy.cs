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

    /// <summary>Not one of the formats documents may have (<see cref="UploadPolicy.AllowedExtensions"/>).</summary>
    NotAllowedType,

    /// <summary>The first bytes are not of the format the extension says (e.g. a text file renamed to .pdf).</summary>
    ContentMismatch,
}

/// <summary>Whether the first bytes of a file are of a format.</summary>
public delegate bool HeaderMatcher(ReadOnlySpan<byte> header);

/// <summary>A format documents may have: its extensions (with the dot) and how its content starts.</summary>
public sealed record UploadFormat(string Name, IReadOnlyList<string> Extensions, HeaderMatcher Matches);

/// <summary>
/// What may be uploaded as a process document: PDF, DOCX, XLSX, JPG/JPEG, PNG and HEIC/HEIF (phone photos), each checked
/// by extension and by its first bytes; never anything else. Executables, scripts and web pages keep their own refusal
/// (and log line). Files are always downloaded as attachments and photos are kept as sent, metadata included (the place
/// and time of a site visit are proof). Malware scanning is a later step.
/// </summary>
public static class UploadPolicy
{
    public const long MaxBytes = 25 * 1024 * 1024;
    public const int MaxFileNameLength = 200;
    private const int MaxExtensionLength = 20;

    /// <summary>Bytes <see cref="Check"/> needs to look at (a HEIC photo says so in its 9th to 12th bytes).</summary>
    public const int HeaderLength = 12;

    /// <summary>
    /// The formats accepted, in one place (one day it may be each consultancy's choice). DOCX and XLSX are ZIP packages;
    /// HEIC/HEIF start with an ISO "ftyp" box naming an image brand.
    /// </summary>
    public static readonly IReadOnlyList<UploadFormat> AllowedFormats =
    [
        new("PDF", [".pdf"], header => header.StartsWith("%PDF"u8)),
        new("DOCX", [".docx"], IsZip),
        new("XLSX", [".xlsx"], IsZip),
        new("JPG", [".jpg", ".jpeg"], header => header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])),
        new("PNG", [".png"], header => header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])),
        new("HEIC", [".heic", ".heif"], IsHeif),
    ];

    public static IReadOnlyList<string> AllowedExtensions { get; } = AllowedFormats.SelectMany(f => f.Extensions).ToList();

    /// <summary>Image brands of HEIF (HEIC is HEIF with HEVC); video brands (e.g. "mp42") are not photos.</summary>
    private static readonly byte[][] HeifBrands =
        ["heic"u8.ToArray(), "heix"u8.ToArray(), "hevc"u8.ToArray(), "hevx"u8.ToArray(), "mif1"u8.ToArray(), "msf1"u8.ToArray(), "heif"u8.ToArray()];

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
        var format = AllowedFormats.FirstOrDefault(f => f.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
        if (format is null)
            return UploadCheck.NotAllowedType;
        return format.Matches(header) ? UploadCheck.Accepted : UploadCheck.ContentMismatch;
    }

    /// <summary>For messages: "PDF, DOCX, XLSX, JPG, PNG, HEIC".</summary>
    public static string AllowedFormatNames => string.Join(", ", AllowedFormats.Select(f => f.Name));

    private static bool IsZip(ReadOnlySpan<byte> header) => header.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04]);

    private static bool IsHeif(ReadOnlySpan<byte> header)
    {
        if (header.Length < 12 || !header[4..8].SequenceEqual("ftyp"u8))
            return false;
        var brand = header[8..12];
        foreach (var known in HeifBrands)
            if (brand.SequenceEqual(known))
                return true;
        return false;
    }

    /// <summary>Office formats that can carry macros (accepted before the formats were limited): flagged to whoever opens them.</summary>
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
