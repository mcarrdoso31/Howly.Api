using System.Security.Cryptography;

namespace BZ.Howly.Api.Core.Shared;

/// <summary>
/// New ids for the entities. Random but growing with time in the order SQL Server sorts <c>uniqueidentifier</c>
/// (its last 6 bytes first), so inserts land at the end of the clustered indexes instead of splitting pages.
/// </summary>
public static class Ids
{
    public static Guid New()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes[..10]);

        // Milliseconds since the Unix epoch, big-endian, in the 6 bytes SQL Server compares first.
        var milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var i = 15; i >= 10; i--)
        {
            bytes[i] = (byte)milliseconds;
            milliseconds >>= 8;
        }

        return new Guid(bytes);
    }
}
