namespace BlogEngine.Server.Storage;

/// <summary>
/// Where media files live (design 9.3). Keys are slash-separated paths such as
/// <c>ab12cd34ef56/original.jpg</c> or <c>ab12cd34ef56/v2/current.jpg</c>; see <see cref="MediaStorageKeys"/>.
/// </summary>
/// <remarks>
/// The file system implementation (<see cref="FileSystemMediaStorage"/>) is the default; an Azure Blob
/// implementation can be added later (T5.8) without touching the callers.
/// </remarks>
public interface IMediaStorage
{
    /// <summary>Writes <paramref name="content"/> under <paramref name="key"/>, replacing any existing object.</summary>
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct);

    /// <summary>Opens the object for reading, or returns <see langword="null"/> when there is none.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);

    /// <summary>Deletes the object; deleting a missing object is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken ct);

    /// <summary>
    /// Deletes every object whose key starts with <paramref name="prefix"/>, for example all versions of one
    /// media item (<c>ab12cd34ef56/</c>).
    /// </summary>
    Task DeletePrefixAsync(string prefix, CancellationToken ct);
}
