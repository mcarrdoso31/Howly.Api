using BZ.Howly.Api.Core.Shared.Exceptions;

namespace BZ.Howly.Api.Core.Processes;

/// <summary>
/// The version of a process as the clients see it (an opaque text, also its ETag) and the check of the version a screen
/// acted on (If-Match): someone changed the process since that screen loaded it, so the action is refused.
/// </summary>
public static class ProcessVersion
{
    public const string StaleCode = "stale_version";

    public static string Of(Process process) => process.Version.ToString("N");

    /// <summary>
    /// Refuses (409 <see cref="StaleCode"/>) when <paramref name="ifMatch"/> names other versions only. Nothing said, or
    /// "*", accepts any: clients that do not send it act as before.
    /// </summary>
    public static void Require(Process process, string? ifMatch)
    {
        if (!Matches(process.Version, ifMatch))
            throw new ConflictException("This process changed since you loaded it. Reload it to see how it is now.", StaleCode);
    }

    /// <summary>If-Match holds one or more entity tags, quoted or not (weak ones too); "*" matches anything.</summary>
    public static bool Matches(Guid version, string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
            return true;

        foreach (var part in ifMatch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "*")
                return true;
            var tag = part.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ? part[2..] : part;
            if (Guid.TryParse(tag.Trim('"'), out var expected) && expected == version)
                return true;
        }

        return false;
    }
}
