using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Markdown;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.Logging;

namespace BlogEngine.Client.Services;

/// <summary>
/// Media metadata for rendering library images in the editor preview (design 9.5, T2.7): remembers what
/// <see cref="IMediaService.LookupAsync"/> returned, so each image is fetched once per page rather than on every
/// keystroke. Scoped, so a new page (or a reload) sees fresh sizes and versions.
/// </summary>
/// <remarks>
/// Ids the library doesn't know are remembered as missing too, so a deleted image doesn't cause a request per
/// keystroke; <see cref="Forget"/> clears one, for example after an upload that makes it known.
/// </remarks>
public sealed class MediaLookupCache(IMediaService mediaService, ILogger<MediaLookupCache> logger) : IMediaLookup
{
    private readonly Dictionary<string, MediaLookupItem?> items = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public MediaLookupItem? Find(string publicId)
    {
        return items.GetValueOrDefault(publicId);
    }

    /// <summary>Whether every id has been looked up (found or known to be missing).</summary>
    public bool HasAll(IEnumerable<string> publicIds)
    {
        return publicIds.All(items.ContainsKey);
    }

    /// <summary>
    /// Looks up the ids not seen before. A failed request is logged and the ids are left unknown, so they render as
    /// missing now and are tried again next time.
    /// </summary>
    /// <returns><see langword="true"/> when anything was added.</returns>
    public async Task<bool> LoadAsync(IEnumerable<string> publicIds, CancellationToken cancellationToken = default)
    {
        var unknown = publicIds.Where(id => !items.ContainsKey(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count == 0)
        {
            return false;
        }

        try
        {
            var found = await mediaService.LookupAsync(unknown, cancellationToken);
            foreach (var id in unknown)
            {
                items[id] = null;
            }

            foreach (var item in found)
            {
                items[item.PublicId] = item;
            }

            return true;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Looking up {Count} media items for the preview failed.", unknown.Count);
            return false;
        }
    }

    /// <summary>Adds or refreshes an item the page already has, such as one just uploaded or edited.</summary>
    public void Remember(MediaItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        items[item.PublicId] = new MediaLookupItem(item.Id, item.PublicId, item.FileName, item.Width, item.Height, item.Version, item.AltText);
    }

    /// <summary>Forgets an id so the next <see cref="LoadAsync"/> asks the server again.</summary>
    public void Forget(string publicId)
    {
        items.Remove(publicId);
    }
}
