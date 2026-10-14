namespace BZ.Howly.Api.Core.Interfaces.Storage;

/// <summary>
/// Where uploaded files live (local disk today, maybe object storage later). Files are addressed by the keys this
/// storage generates, never by names coming from users.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores the content under <paramref name="folder"/> with a new unique name and returns its key.</summary>
    Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Null when the file does not exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Does nothing when the file does not exist.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
