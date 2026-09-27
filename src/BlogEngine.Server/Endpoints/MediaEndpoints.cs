using System.Globalization;

using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Media;
using BlogEngine.Server.Storage;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace BlogEngine.Server.Endpoints;

/// <summary>
/// Serves library images to readers at <c>/media/{publicId}/{fileName}</c> (design 9.4, T2.4).
/// </summary>
/// <remarks>
/// <para>
/// Always serves the current version. Rendered posts link to <c>?v={version}</c>; when that matches the current
/// version the response is cached for a year as <c>immutable</c>, since an edit changes the URL. Any other request
/// (no or an old <c>v</c>) gets a short cache lifetime and must revalidate with the <c>ETag</c>, which answers
/// <c>304 Not Modified</c> until the image changes.
/// </para>
/// <para>
/// <b>Renditions</b> (design 9.4, T4.19): <c>?w=640</c> serves the smallest rendition at least 640 pixels wide (the
/// largest one if none is), and <c>?f=webp</c> picks the WebP renditions instead of the image's own format. Without
/// either, or when the item has no renditions (GIFs, or images the backfill hasn't reached), the current version is
/// served at full size, so a rendition URL never breaks.
/// </para>
/// <para>
/// The <c>Content-Type</c> is the one detected when the file was decoded, and <c>nosniff</c> stops browsers from
/// second-guessing it. Each request reads one row by its unique public id; the long browser cache keeps that rare.
/// </para>
/// </remarks>
public static class MediaEndpoints
{
    /// <summary><c>Cache-Control</c> for a request whose <c>?v=</c> is the current version.</summary>
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary><c>Cache-Control</c> for any other request: cache briefly, then revalidate with the ETag.</summary>
    public const string RevalidateCacheControl = "public, max-age=300, must-revalidate";

    /// <summary>Maps the public media endpoint.</summary>
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet($"{MediaPaths.Prefix}/{{publicId}}/{{fileName}}", ServeAsync)
            .AllowAnonymous()
            .WithName("Media");

        return endpoints;
    }

    /// <summary>Streams the current version of an item or one of its renditions, or answers 304/404.</summary>
    private static async Task<IResult> ServeAsync(string publicId, string fileName, string? v, string? w, string? f, HttpContext context,
        ApplicationDbContext dbContext, IMediaStorage storage, CancellationToken cancellationToken)
    {
        if (publicId.Length != FieldLengths.MediaPublicId)
        {
            return TypedResults.NotFound();
        }

        var item = await dbContext.MediaItems
            .AsNoTracking()
            .Where(m => m.PublicId == publicId)
            .Select(m => new { m.Id, m.FileName, m.CurrentStorageKey, m.ContentType, m.Version })
            .SingleOrDefaultAsync(cancellationToken);

        // The file name is part of the URL, so a wrong one is a wrong URL rather than an alias.
        if (item is null || !string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.NotFound();
        }

        // A requested width or format picks a rendition; everything else is the current version.
        var storageKey = item.CurrentStorageKey;
        var contentType = item.ContentType;
        var variant = string.Empty;
        var width = int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out var requested) && requested > 0 ? requested : (int?)null;
        var wantsWebp = string.Equals(f, MediaRenditionWriter.WebpFormat, StringComparison.OrdinalIgnoreCase);
        if (width is not null || wantsWebp)
        {
            var renditions = await dbContext.MediaRenditions
                .AsNoTracking()
                .Where(r => r.MediaItemId == item.Id)
                .Select(r => new MediaRenditionChoice(r.Width, r.Format, r.StorageKey))
                .ToListAsync(cancellationToken);

            if (ChooseRendition(renditions, width, wantsWebp ? MediaRenditionWriter.WebpFormat : null) is { } rendition)
            {
                storageKey = rendition.StorageKey;
                contentType = rendition.Format == MediaRenditionWriter.WebpFormat ? "image/webp" : item.ContentType;
                variant = string.Create(CultureInfo.InvariantCulture, $"-{rendition.Width}{rendition.Format}");
            }
        }

        var entityTag = new EntityTagHeaderValue($"\"{publicId}-{item.Version.ToString(CultureInfo.InvariantCulture)}{variant}\"");
        var isCurrentVersion = int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version == item.Version;

        var headers = context.Response.Headers;
        headers.CacheControl = isCurrentVersion ? ImmutableCacheControl : RevalidateCacheControl;
        headers.XContentTypeOptions = "nosniff";
        headers.ETag = entityTag.ToString();

        if (context.Request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } ifNoneMatch
            && ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(entityTag, useStrongComparison: false)))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        var content = await storage.OpenReadAsync(storageKey, cancellationToken);
        return content is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(content, contentType, entityTag: entityTag, enableRangeProcessing: true);
    }

    /// <summary>
    /// The rendition to serve for <c>?w=</c> and <c>?f=</c>: among those in <paramref name="format"/> (the WebP ones, or
    /// otherwise those in the image's own format), the smallest at least <paramref name="width"/> wide, else the largest.
    /// Without a width, the largest. <see langword="null"/> when there is none in that format.
    /// </summary>
    public static MediaRenditionChoice? ChooseRendition(IReadOnlyCollection<MediaRenditionChoice> renditions, int? width, string? format)
    {
        var candidates = renditions
            .Where(r => format is null ? r.Format != MediaRenditionWriter.WebpFormat : r.Format == format)
            .OrderBy(r => r.Width)
            .ToList();

        // A WebP original has only WebP renditions, which are then also its "own format".
        if (candidates.Count == 0 && format is null)
        {
            candidates = [.. renditions.OrderBy(r => r.Width)];
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return width is { } wanted ? candidates.FirstOrDefault(r => r.Width >= wanted) ?? candidates[^1] : candidates[^1];
    }

    /// <summary>A stored rendition, as <see cref="ChooseRendition"/> sees it.</summary>
    public sealed record MediaRenditionChoice(int Width, string Format, string StorageKey);
}