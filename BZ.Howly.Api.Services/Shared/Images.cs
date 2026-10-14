using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Services.Shared;

/// <summary>Uploaded images (avatars, logos): read with a size limit and recognized by their first bytes.</summary>
internal static class Images
{
    /// <summary>
    /// The image in memory, or the error for the "file" field: too big, or not PNG/JPEG/WebP. Reads at most one byte
    /// over the limit: enough to know the file is too big without loading all of it.
    /// </summary>
    public static async Task<(MemoryStream? Content, ImageFormat? Format, string? Error)> ReadAsync(
        Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while (total <= maxBytes && (read = await source.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, maxBytes + 1 - total)), cancellationToken)) > 0)
        {
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            total += read;
        }

        if (buffer.Length > maxBytes)
            return Fail($"The image must have at most {maxBytes / (1024 * 1024)} MB.");

        var format = ImageFormat.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, ImageFormat.HeaderLength)));
        if (format is null)
            return Fail("Only PNG, JPEG and WebP images are accepted.");

        buffer.Position = 0;
        return (buffer, format, null);

        (MemoryStream?, ImageFormat?, string?) Fail(string error)
        {
            buffer.Dispose();
            return (null, null, error);
        }
    }
}
