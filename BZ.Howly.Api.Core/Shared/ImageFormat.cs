namespace BZ.Howly.Api.Core.Shared;

/// <summary>
/// Image formats accepted for avatars and logos, recognized by their first bytes (never by the file name).
/// SVG is left out on purpose: it can carry scripts.
/// </summary>
public sealed record ImageFormat(string ContentType, string Extension)
{
    public static readonly ImageFormat Png = new("image/png", ".png");
    public static readonly ImageFormat Jpeg = new("image/jpeg", ".jpg");
    public static readonly ImageFormat WebP = new("image/webp", ".webp");

    private static readonly ImageFormat[] All = [Png, Jpeg, WebP];

    /// <summary>Bytes needed by <see cref="Detect"/>.</summary>
    public const int HeaderLength = 12;

    public static ImageFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderLength)
            return null;

        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return Png;

        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return Jpeg;

        if (header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return WebP;

        return null;
    }

    public static ImageFormat? FromExtension(string extension) =>
        All.FirstOrDefault(f => string.Equals(f.Extension, extension, StringComparison.OrdinalIgnoreCase));
}
