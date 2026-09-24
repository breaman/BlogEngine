using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Markdown;

/// <summary>
/// Supplies media library metadata to <see cref="MediaLinkRewriter"/> while Markdown is rendered (design 9.4).
/// </summary>
/// <remarks>
/// Rendering is synchronous, so the caller loads the items first (the server from the database, the WebAssembly
/// preview through <c>IMediaService.LookupAsync</c>) and passes them in as a <see cref="MediaLookup"/>.
/// </remarks>
public interface IMediaLookup
{
    /// <summary>The item with this public id, or <see langword="null"/> when it isn't in the library.</summary>
    MediaLookupItem? Find(string publicId);
}

/// <summary>An <see cref="IMediaLookup"/> over a fixed set of items.</summary>
/// <example>
/// <code>
/// var lookup = new MediaLookup(await mediaService.LookupAsync(MediaReferenceScanner.FindPublicIds(markdown)));
/// var html = BlogMarkdownPipeline.Default.RenderPost(markdown, lookup).Html;
/// </code>
/// </example>
public sealed class MediaLookup : IMediaLookup
{
    private readonly Dictionary<string, MediaLookupItem> items;

    /// <summary>Creates a lookup over <paramref name="items"/>.</summary>
    public MediaLookup(IEnumerable<MediaLookupItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        this.items = items.ToDictionary(i => i.PublicId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A lookup with no items: every library image renders as missing.</summary>
    public static MediaLookup Empty { get; } = new([]);

    /// <summary>The items, for usage tracking.</summary>
    public IReadOnlyCollection<MediaLookupItem> Items => items.Values;

    /// <inheritdoc />
    public MediaLookupItem? Find(string publicId)
    {
        return items.GetValueOrDefault(publicId);
    }
}
