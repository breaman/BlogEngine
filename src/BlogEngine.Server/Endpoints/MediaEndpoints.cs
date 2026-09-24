using System.Globalization;

using BlogEngine.Data.Models;
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
/// <c>304 Not Modified</c> until the image changes. <c>?w=</c> renditions arrive with T4.19.
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

    /// <summary>Streams the current version of an item, or answers 304/404.</summary>
    private static async Task<IResult> ServeAsync(string publicId, string fileName, string? v, HttpContext context,
        ApplicationDbContext dbContext, IMediaStorage storage, CancellationToken cancellationToken)
    {
        if (publicId.Length != FieldLengths.MediaPublicId)
        {
            return TypedResults.NotFound();
        }

        var item = await dbContext.MediaItems
            .AsNoTracking()
            .Where(m => m.PublicId == publicId)
            .Select(m => new { m.FileName, m.CurrentStorageKey, m.ContentType, m.Version })
            .SingleOrDefaultAsync(cancellationToken);

        // The file name is part of the URL, so a wrong one is a wrong URL rather than an alias.
        if (item is null || !string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.NotFound();
        }

        var entityTag = new EntityTagHeaderValue($"\"{publicId}-{item.Version.ToString(CultureInfo.InvariantCulture)}\"");
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

        var content = await storage.OpenReadAsync(item.CurrentStorageKey, cancellationToken);
        return content is null
            ? TypedResults.NotFound()
            : TypedResults.Stream(content, item.ContentType, entityTag: entityTag, enableRangeProcessing: true);
    }
}
