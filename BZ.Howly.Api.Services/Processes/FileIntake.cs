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
        // Only the first bytes are read here, to check the type; the rest goes straight to the storage.
        var header = new byte[UploadPolicy.HeaderLength];
        var headerLength = 0;
        int read;
        while (headerLength < header.Length && (read = await file.Content.ReadAsync(header.AsMemory(headerLength), cancellationToken)) > 0)
            headerLength += read;

        var check = UploadPolicy.Check(file.FileName, header.AsSpan(0, headerLength));
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

        var content = new CappedStream(header.AsMemory(0, headerLength), file.Content, UploadPolicy.MaxBytes);
        string key;
        try
        {
            key = await fileStorage.SaveAsync(folder, StoredExtension, content, cancellationToken);
        }
        catch (FileTooLargeException)
        {
            // The storage leaves nothing behind for a copy cut halfway.
            return (null, FileError("The file must have at most 25 MB."));
        }

        return (new StoredFile(key, UploadPolicy.CleanFileName(file.FileName), content.BytesRead), null);
    }

    private sealed class FileTooLargeException() : Exception("The file is over the upload limit.");

    /// <summary>The header already read, then the rest of the upload; stops the copy once it goes over the limit.</summary>
    private sealed class CappedStream(ReadOnlyMemory<byte> header, Stream rest, long maxBytes) : Stream
    {
        private ReadOnlyMemory<byte> _header = header;

        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read;
            if (!_header.IsEmpty)
            {
                read = Math.Min(buffer.Length, _header.Length);
                _header[..read].CopyTo(buffer);
                _header = _header[read..];
            }
            else
            {
                read = await rest.ReadAsync(buffer, cancellationToken);
            }

            return Count(read);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read;
            if (!_header.IsEmpty)
            {
                read = Math.Min(count, _header.Length);
                _header.Span[..read].CopyTo(buffer.AsSpan(offset, read));
                _header = _header[read..];
            }
            else
            {
                read = rest.Read(buffer, offset, count);
            }

            return Count(read);
        }

        private int Count(int read)
        {
            BytesRead += read;
            if (BytesRead > maxBytes)
                throw new FileTooLargeException();
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static Dictionary<string, string[]> FileError(string message) => new() { ["file"] = [message] };
}
