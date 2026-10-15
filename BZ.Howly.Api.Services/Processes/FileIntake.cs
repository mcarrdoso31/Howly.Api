using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Interfaces.Storage;
using BZ.Howly.Api.Core.Processes;
using BZ.Howly.Api.Core.Shared;
using Microsoft.Extensions.Logging;

namespace BZ.Howly.Api.Services.Processes;

/// <summary>Receiving an uploaded file: the same checks for document versions and examples.</summary>
internal static class FileIntake
{
    /// <summary>Files keep a neutral extension on disk: the original name lives in the database and comes back on download.</summary>
    private const string StoredExtension = ".bin";

    /// <summary>Checks and stores the file under <paramref name="folder"/>; on a refusal the errors point to the "file" field.</summary>
    public static async Task<(StoredFile? File, Dictionary<string, string[]>? Errors)> StoreAsync(
        IFileStorage fileStorage, string folder, IncomingFile file, ILogger logger, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await file.Content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > UploadPolicy.MaxBytes)
                return (null, FileError("The file must have at most 25 MB."));
            buffer.Write(chunk, 0, read);
        }

        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, UploadPolicy.HeaderLength));
        var check = UploadPolicy.Check(file.FileName, header);
        if (check != UploadCheck.Accepted)
        {
            if (check is UploadCheck.BlockedType or UploadCheck.BlockedContent)
                // Only the extension: file names often carry people's names (LGPD).
                logger.LogWarning("Refused upload ({Extension}): {Check}.", Path.GetExtension(UploadPolicy.CleanFileName(file.FileName)), check);
            return (null, FileError(check switch
            {
                UploadCheck.BlockedType or UploadCheck.BlockedContent => "Executables, scripts and web pages are not accepted.",
                UploadCheck.Empty => "The file is empty.",
                _ => "The file needs a name with an extension.",
            }));
        }

        buffer.Position = 0;
        var key = await fileStorage.SaveAsync(folder, StoredExtension, buffer, cancellationToken);
        return (new StoredFile(key, UploadPolicy.CleanFileName(file.FileName), buffer.Length), null);
    }

    private static Dictionary<string, string[]> FileError(string message) => new() { ["file"] = [message] };
}
