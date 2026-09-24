namespace BlogEngine.Server.Services.Media;

/// <summary>
/// An image encoded by <see cref="MediaProcessor"/>, ready to store.
/// </summary>
/// <param name="Content">The encoded bytes.</param>
/// <param name="ContentType">Media type of the encoding, such as <c>image/jpeg</c>.</param>
/// <param name="Extension">File extension without the dot, such as <c>jpg</c>.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Hash">Lowercase hex SHA-256 of <paramref name="Content"/>.</param>
public sealed record ProcessedImage(byte[] Content, string ContentType, string Extension, int Width, int Height, string Hash)
{
    /// <summary>Size of <see cref="Content"/> in bytes.</summary>
    public long SizeBytes => Content.LongLength;

    /// <summary>A read-only stream over <see cref="Content"/>.</summary>
    public Stream OpenRead()
    {
        return new MemoryStream(Content, writable: false);
    }
}
