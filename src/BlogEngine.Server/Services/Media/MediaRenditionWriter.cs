using BlogEngine.Data.Models;
using BlogEngine.Server.Storage;
using BlogEngine.Shared.Services;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Media;

/// <summary>
/// Generates and stores the responsive renditions of a media item (design 9.4, M6, T4.19): resized WebP copies, plus
/// copies in the image's own format, at the configured rendition widths. Run on upload, after every edit, and by the
/// backfill for items uploaded before renditions existed or before the widths setting changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Widths</b> (<see cref="PlanWidths"/>): every configured width narrower than the image, plus the image's own width
/// when it is no wider than the largest configured one, so a 1000 px photo still gets a full-size WebP. Wider images
/// stop at the largest configured width, which covers the post column on high-density screens.
/// </para>
/// <para>
/// <b>Files and rows</b> follow the media service's rule: new files are written before the rows that point at them,
/// and files no row uses any more are deleted afterwards, so a failure only ever leaves an orphaned file.
/// </para>
/// </remarks>
public sealed class MediaRenditionWriter(
    ApplicationDbContext dbContext,
    IMediaStorage storage,
    MediaProcessor processor,
    ISettingsService settingsService,
    ILogger<MediaRenditionWriter> logger)
{
    /// <summary>Format name of the WebP renditions, the ones listed in <c>srcset</c>.</summary>
    public const string WebpFormat = "webp";

    /// <summary>
    /// The rendition widths for an image <paramref name="imageWidth"/> pixels wide (see the class remarks), smallest first.
    /// </summary>
    /// <example>
    /// <code>
    /// PlanWidths(1000, [320, 640, 960, 1280, 1920]); // [320, 640, 960, 1000]
    /// PlanWidths(4000, [320, 640, 960, 1280, 1920]); // [320, 640, 960, 1280, 1920]
    /// PlanWidths(200, [320, 640]);                   // [200]
    /// </code>
    /// </example>
    public static IReadOnlyList<int> PlanWidths(int imageWidth, IEnumerable<int> configuredWidths)
    {
        ArgumentNullException.ThrowIfNull(configuredWidths);

        var configured = configuredWidths.Where(w => w > 0).Distinct().Order().ToList();
        if (imageWidth <= 0 || configured.Count == 0)
        {
            return [];
        }

        var widths = configured.Where(w => w < imageWidth).ToList();
        if (imageWidth <= configured[^1])
        {
            widths.Add(imageWidth);
        }

        return widths;
    }

    /// <summary>The formats an item gets renditions in: WebP, plus its own format unless that is WebP; none for GIF.</summary>
    public static IReadOnlyList<string> PlanFormats(string contentType)
    {
        return contentType switch
        {
            "image/gif" => [],
            "image/webp" => [WebpFormat],
            "image/png" => [WebpFormat, "png"],
            _ => [WebpFormat, "jpg"]
        };
    }

    /// <summary>
    /// Whether an item's renditions are missing or out of date: made for another version, or for other widths than the
    /// settings now ask for.
    /// </summary>
    public static bool IsOutdated(int width, string contentType, int version, string publicId,
        IReadOnlyCollection<(int Width, string Format, string StorageKey)> renditions, IEnumerable<int> configuredWidths)
    {
        ArgumentNullException.ThrowIfNull(renditions);

        var formats = PlanFormats(contentType);
        var expected = PlanWidths(width, configuredWidths)
            .SelectMany(w => formats.Select(f => (w, f)))
            .ToHashSet();
        var actual = renditions.Select(r => (r.Width, r.Format)).ToHashSet();
        var versionPrefix = $"{publicId}/v{version}/";

        return !expected.SetEquals(actual) || renditions.Any(r => !r.StorageKey.StartsWith(versionPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Replaces the renditions of <paramref name="item"/> (tracked by the context) with fresh ones made from its current
    /// version, and saves. Files of the previous renditions that the new ones don't overwrite are deleted afterwards.
    /// </summary>
    /// <param name="item">The item, as saved; its <see cref="MediaItem.Version"/> decides where the files go.</param>
    /// <param name="current">The bytes of its current version, or <see langword="null"/> to read them from storage.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The number of renditions stored; 0 when the item gets none (a GIF) or they couldn't be made.</returns>
    /// <remarks>
    /// A rendition failure never fails the caller's upload or edit: the image is still served at full size, which is
    /// logged, and the backfill will try again.
    /// </remarks>
    public async Task<int> WriteAsync(MediaItem item, byte[]? current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var settings = await settingsService.GetAsync(cancellationToken);
        var old = await dbContext.MediaRenditions.Where(r => r.MediaItemId == item.Id).ToListAsync(cancellationToken);

        IReadOnlyList<ProcessedRendition> made;
        try
        {
            current ??= await ReadCurrentAsync(item, cancellationToken);
            made = current is null
                ? []
                : await processor.CreateRenditionsAsync(current, PlanWidths(item.Width, settings.RenditionWidths), cancellationToken);
        }
        catch (MediaProcessingException ex)
        {
            logger.LogWarning(ex, "Renditions of media {MediaId} ({PublicId}) couldn't be made; it is served at full size.", item.Id, item.PublicId);
            made = [];
        }

        var renditions = new List<MediaRendition>(made.Count);
        foreach (var rendition in made)
        {
            var key = MediaStorageKeys.Rendition(item.PublicId, item.Version, rendition.Width, rendition.Format);
            await using (var content = rendition.OpenRead())
            {
                await storage.SaveAsync(key, content, rendition.ContentType, cancellationToken);
            }

            renditions.Add(new MediaRendition
            {
                MediaItemId = item.Id,
                Width = rendition.Width,
                Format = rendition.Format,
                StorageKey = key,
                SizeBytes = rendition.SizeBytes
            });
        }

        dbContext.MediaRenditions.RemoveRange(old);
        dbContext.MediaRenditions.AddRange(renditions);
        await dbContext.SaveChangesAsync(cancellationToken);

        var kept = renditions.Select(r => r.StorageKey).ToHashSet(StringComparer.Ordinal);
        foreach (var key in old.Select(r => r.StorageKey).Where(k => !kept.Contains(k)))
        {
            await DeleteQuietlyAsync(key);
        }

        return renditions.Count;
    }

    /// <summary>Reads the current version from storage; <see langword="null"/> (logged) when the file is missing.</summary>
    private async Task<byte[]?> ReadCurrentAsync(MediaItem item, CancellationToken cancellationToken)
    {
        await using var stream = await storage.OpenReadAsync(item.CurrentStorageKey, cancellationToken);
        if (stream is null)
        {
            logger.LogError("The current version of media {MediaId} is missing from storage at {StorageKey}.", item.Id, item.CurrentStorageKey);
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>Deletes a file that is no longer referenced; a failure only leaves an orphan behind.</summary>
    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Deleting the old rendition {StorageKey} failed; it is no longer used.", key);
        }
    }
}
