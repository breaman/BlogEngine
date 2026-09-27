using System.Text.Json;

using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.Server.Storage;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Media;

/// <summary>
/// Server implementation of <see cref="IMediaService"/> (design 9), plus uploads, working on
/// <see cref="ApplicationDbContext"/> and <see cref="IMediaStorage"/> directly. Used while prerendering the admin
/// media pages and behind the <c>/api/admin/media</c> endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <b>Files and rows.</b> Files are written before the row that points at them and deleted after the row is gone,
/// so the database never references a missing file. A failure in between can only leave an orphaned file, which is
/// harmless, and the operation cleans up after itself where it can.
/// </para>
/// <para>
/// <b>Posts.</b> Stored post HTML contains each image's size and <c>?v=</c> version, so editing or deleting an image
/// re-renders every post that uses it (<see cref="PostContentRenderer.RerenderAsync"/>) and evicts the public caches.
/// </para>
/// <para>
/// <b>Usage</b> counts posts in the trash as well: restoring a post must not bring back a broken image.
/// </para>
/// <para>
/// <b>Renditions</b> (design 9.4, T4.19) are made by <see cref="MediaRenditionWriter"/> whenever a version is stored
/// (upload, edit, revert, copy), before posts are re-rendered, since the stored HTML lists the rendition widths.
/// </para>
/// </remarks>
public sealed class ServerMediaService(
    ApplicationDbContext dbContext,
    IMediaStorage storage,
    MediaProcessor processor,
    ISettingsService settingsService,
    PostContentRenderer contentRenderer,
    MediaRenditionWriter renditionWriter,
    IValidator<MediaUpdateRequest> updateValidator,
    IValidator<MediaEditOperations> editValidator,
    ILogger<ServerMediaService> logger) : IMediaService
{
    /// <summary>Attempts at picking an unused public id before giving up.</summary>
    private const int MaxPublicIdAttempts = 3;

    /// <summary>Most images one rendition backfill call processes, so each request stays short.</summary>
    public const int MaxRenditionBatch = 20;

    private static readonly JsonSerializerOptions OperationsJsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<PagedResult<MediaItemDto>> GetMediaAsync(MediaListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MediaListQuery.MaxPageSize);

        var items = dbContext.MediaItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Case-insensitive through the database's default collation.
            var term = query.Search.Trim();
            items = items.Where(m => m.FileName.Contains(term) || m.AltText.Contains(term) || (m.Caption != null && m.Caption.Contains(term)));
        }

        if (query.Unused)
        {
            items = items.Where(m => !UsingPosts().Any(p => p.CoverMediaId == m.Id || p.SocialImageMediaId == m.Id
                || p.PostMedia.Any(pm => pm.MediaItemId == m.Id)));
        }

        var totalCount = await items.CountAsync(cancellationToken);
        var rows = await Project(items.OrderByDescending(m => m.Id))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<MediaItemDto>([.. rows.Select(ToDto)], totalCount, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<MediaItemDto?> GetMediaItemAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await Project(dbContext.MediaItems.AsNoTracking().Where(m => m.Id == id)).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var item = ToDto(row);
        item.UsedIn = [.. await LoadUsageAsync(id, cancellationToken)];
        return item;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaLookupItem>> LookupAsync(IReadOnlyCollection<string> publicIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicIds);

        var lookup = await MediaLookupQuery.LoadAsync(dbContext, publicIds, cancellationToken);
        return [.. lookup.Items];
    }

    /// <inheritdoc />
    public async Task<MediaSaveResult> UpdateAsync(int id, MediaUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = updateValidator.Validate(request).ToDictionary();
        if (errors.Count > 0)
        {
            return new MediaInvalid(errors.AsReadOnly());
        }

        var item = await dbContext.MediaItems.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (item is null)
        {
            return MediaSaveResult.NotFound;
        }

        var altText = request.AltText?.Trim() ?? string.Empty;
        var altTextChanged = item.AltText != altText;
        item.AltText = altText;
        item.Caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);

        // The library alt text is the fallback for images inserted without one, so it is baked into stored HTML.
        if (altTextChanged)
        {
            await contentRenderer.RerenderAsync(await LoadUsingPostIdsAsync(id, cancellationToken), cancellationToken);
        }

        return new MediaSaved((await GetMediaItemAsync(id, cancellationToken))!);
    }

    /// <inheritdoc />
    public async Task<MediaSaveResult> EditAsync(int id, MediaEditOperations operations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var errors = editValidator.Validate(operations).ToDictionary();
        if (errors.Count > 0)
        {
            return new MediaInvalid(errors.AsReadOnly());
        }

        return await ApplyEditsAsync(id, operations, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>An edit with no operations, so the result, logging and post updates are exactly an edit's.</remarks>
    public Task<MediaSaveResult> RevertAsync(int id, CancellationToken cancellationToken = default)
    {
        return ApplyEditsAsync(id, new MediaEditOperations(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MediaSaveResult> SaveAsCopyAsync(int id, MediaEditOperations operations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var errors = editValidator.Validate(operations).ToDictionary();
        if (errors.Count > 0)
        {
            return new MediaInvalid(errors.AsReadOnly());
        }

        var source = await dbContext.MediaItems.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (source is null)
        {
            return MediaSaveResult.NotFound;
        }

        var original = await ReadOriginalAsync(source, cancellationToken);
        if (original is null)
        {
            return MediaSaveResult.Invalid(string.Empty, "The original image is missing from storage, so it can't be copied.");
        }

        ProcessedImage edited;
        try
        {
            edited = await processor.ApplyEditsAsync(new MemoryStream(original, writable: false), operations, cancellationToken);
        }
        catch (MediaProcessingException ex)
        {
            return MediaSaveResult.Invalid(string.Empty, ex.Message);
        }

        // The copy keeps the source's original, so it can be edited and reverted on its own later (design 9.2).
        var copy = await StoreNewItemAsync(source.FileName, new StoredOriginal(original, source.ContentType, edited.Extension, source.ContentHash),
            edited, operations, source.AltText, source.Caption, cancellationToken);
        await renditionWriter.WriteAsync(copy, edited.Content, cancellationToken);

        MediaLog.MediaCopied(logger, copy.Id, copy.PublicId, source.Id, copy.Width, copy.Height, copy.SizeBytes);
        return new MediaSaved((await GetMediaItemAsync(copy.Id, cancellationToken))!);
    }

    /// <inheritdoc />
    public async Task<MediaRenditionProgress> GetRenditionProgressAsync(CancellationToken cancellationToken = default)
    {
        return new MediaRenditionProgress(0, (await FindOutdatedRenditionsAsync(cancellationToken)).Count);
    }

    /// <inheritdoc />
    public async Task<MediaRenditionProgress> GenerateRenditionsAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        var outdated = await FindOutdatedRenditionsAsync(cancellationToken);
        var batch = outdated.Take(Math.Clamp(maxItems, 1, MaxRenditionBatch)).ToList();

        var posts = new HashSet<int>();
        foreach (var itemId in batch)
        {
            var item = await dbContext.MediaItems.SingleOrDefaultAsync(m => m.Id == itemId, cancellationToken);
            if (item is null)
            {
                continue;
            }

            await renditionWriter.WriteAsync(item, current: null, cancellationToken);
            posts.UnionWith(await LoadUsingPostIdsAsync(item.Id, cancellationToken));
        }

        // The stored HTML of the posts lists the rendition widths in srcset, so it has to be rendered again.
        await contentRenderer.RerenderAsync(posts, cancellationToken);
        logger.LogInformation("Renditions generated for {Count} media items; {Remaining} remain.", batch.Count, outdated.Count - batch.Count);

        return new MediaRenditionProgress(batch.Count, outdated.Count - batch.Count);
    }

    /// <summary>
    /// Applies <paramref name="operations"/> to the stored original and saves the result as a new version (see
    /// <see cref="IMediaService.EditAsync"/>); with no operations the original becomes current again (revert).
    /// </summary>
    private async Task<MediaSaveResult> ApplyEditsAsync(int id, MediaEditOperations operations, CancellationToken cancellationToken)
    {
        var item = await dbContext.MediaItems.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (item is null)
        {
            return MediaSaveResult.NotFound;
        }

        var original = await ReadOriginalAsync(item, cancellationToken);
        if (original is null)
        {
            return MediaSaveResult.Invalid(string.Empty, "The original image is missing from storage, so it can't be edited.");
        }

        ProcessedImage edited;
        try
        {
            edited = await processor.ApplyEditsAsync(new MemoryStream(original, writable: false), operations, cancellationToken);
        }
        catch (MediaProcessingException ex)
        {
            return MediaSaveResult.Invalid(string.Empty, ex.Message);
        }

        var previousKey = item.CurrentStorageKey;
        item.Version++;
        if (operations.IsIdentity)
        {
            // Back to the untouched original: no need to store a copy of it.
            item.CurrentStorageKey = item.OriginalStorageKey;
            item.EditOperationsJson = null;
            item.SizeBytes = original.LongLength;
        }
        else
        {
            item.CurrentStorageKey = MediaStorageKeys.Version(item.PublicId, item.Version, edited.Extension);
            item.EditOperationsJson = JsonSerializer.Serialize(operations, OperationsJsonOptions);
            item.SizeBytes = edited.SizeBytes;

            await using var content = edited.OpenRead();
            await storage.SaveAsync(item.CurrentStorageKey, content, edited.ContentType, cancellationToken);
        }

        item.Width = edited.Width;
        item.Height = edited.Height;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (item.CurrentStorageKey != item.OriginalStorageKey)
            {
                await storage.DeleteAsync(item.CurrentStorageKey, CancellationToken.None);
            }

            throw;
        }

        if (previousKey != item.OriginalStorageKey && previousKey != item.CurrentStorageKey)
        {
            await DeleteQuietlyAsync(previousKey);
        }

        // Renditions of the new version first: re-rendered posts pick up their widths.
        await renditionWriter.WriteAsync(item, operations.IsIdentity ? original : edited.Content, cancellationToken);

        var postCount = await contentRenderer.RerenderAsync(await LoadUsingPostIdsAsync(id, cancellationToken), cancellationToken);
        MediaLog.MediaEdited(logger, item.Id, item.PublicId, item.Version, item.Width, item.Height, item.SizeBytes, postCount);

        return new MediaSaved((await GetMediaItemAsync(id, cancellationToken))!);
    }

    /// <summary>
    /// Ids of the items whose renditions are missing or out of date (<see cref="MediaRenditionWriter.IsOutdated"/>),
    /// oldest first. Reads only the columns involved, which stays small for a personal library.
    /// </summary>
    private async Task<List<int>> FindOutdatedRenditionsAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        var items = await dbContext.MediaItems
            .AsNoTracking()
            .OrderBy(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.PublicId,
                m.Width,
                m.ContentType,
                m.Version,
                Renditions = m.Renditions.Select(r => new { r.Width, r.Format, r.StorageKey }).ToList()
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. items
                .Where(m => MediaRenditionWriter.IsOutdated(m.Width, m.ContentType, m.Version, m.PublicId,
                    [.. m.Renditions.Select(r => (r.Width, r.Format, r.StorageKey))], settings.RenditionWidths))
                .Select(m => m.Id)
        ];
    }

    /// <summary>The stored original of an item, or <see langword="null"/> (logged) when the file is missing.</summary>
    private async Task<byte[]?> ReadOriginalAsync(MediaItem item, CancellationToken cancellationToken)
    {
        await using var stream = await storage.OpenReadAsync(item.OriginalStorageKey, cancellationToken);
        if (stream is null)
        {
            logger.LogError("The original of media {MediaId} is missing from storage at {StorageKey}.", item.Id, item.OriginalStorageKey);
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <inheritdoc />
    public async Task<MediaDeleteResult> DeleteAsync(int id, bool force, CancellationToken cancellationToken = default)
    {
        var item = await dbContext.MediaItems.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (item is null)
        {
            return MediaDeleteResult.NotFound;
        }

        var usage = await LoadUsageAsync(id, cancellationToken);
        if (usage.Count > 0 && !force)
        {
            return new MediaInUse(usage);
        }

        // Cover, social image and settings references don't cascade (SQL Server allows only one cascade path), so
        // clear them.
        var referencing = await UsingPosts().Where(p => p.CoverMediaId == id || p.SocialImageMediaId == id).ToListAsync(cancellationToken);
        foreach (var post in referencing)
        {
            post.CoverMediaId = post.CoverMediaId == id ? null : post.CoverMediaId;
            post.SocialImageMediaId = post.SocialImageMediaId == id ? null : post.SocialImageMediaId;
        }

        var settings = await dbContext.SiteSettings
            .Where(s => s.AuthorAvatarMediaId == id || s.FaviconMediaId == id || s.DefaultSocialImageMediaId == id)
            .ToListAsync(cancellationToken);
        foreach (var row in settings)
        {
            row.AuthorAvatarMediaId = row.AuthorAvatarMediaId == id ? null : row.AuthorAvatarMediaId;
            row.FaviconMediaId = row.FaviconMediaId == id ? null : row.FaviconMediaId;
            row.DefaultSocialImageMediaId = row.DefaultSocialImageMediaId == id ? null : row.DefaultSocialImageMediaId;
        }

        // PostMedia and renditions cascade from the item.
        dbContext.MediaItems.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        await storage.DeletePrefixAsync(MediaStorageKeys.ItemPrefix(item.PublicId), CancellationToken.None);
        if (settings.Count > 0)
        {
            // Saving the settings again evicts their cache and everything derived from them.
            var current = await settingsService.GetAsync(CancellationToken.None);
            await settingsService.SaveAsync(current, CancellationToken.None);
        }

        // Published posts drop the image; drafts show the "missing image" placeholder in the editor preview.
        var postCount = await contentRenderer.RerenderAsync([.. usage.Select(u => u.PostId)], CancellationToken.None);
        MediaLog.MediaDeleted(logger, item.Id, item.PublicId, postCount);

        return MediaDeleteResult.Deleted;
    }

    /// <summary>
    /// Adds one uploaded file to the library (design 9.1, T2.3): checks its size against the settings, decodes and
    /// cleans it with <see cref="MediaProcessor"/>, stores it as the original (which is also the current version) and
    /// reports existing items with the same content.
    /// </summary>
    /// <param name="fileName">The name the browser sent.</param>
    /// <param name="content">The file's bytes.</param>
    /// <param name="length">The file's size, checked before anything is decoded.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <returns>The new item, or the reason the file was rejected; never throws for a bad file.</returns>
    public async Task<MediaUploadResult> UploadAsync(string fileName, Stream content, long length, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var result = new MediaUploadResult { FileName = fileName };
        var settings = await settingsService.GetAsync(cancellationToken);
        var maxBytes = settings.MaxUploadSizeMegabytes * 1024L * 1024L;

        if (length == 0)
        {
            return Reject(result, length, "The file is empty.");
        }

        if (length > maxBytes)
        {
            return Reject(result, length,
                $"The file is {length / (1024.0 * 1024.0):0.#} MB, which is over the {settings.MaxUploadSizeMegabytes} MB limit.");
        }

        ProcessedImage image;
        try
        {
            image = await processor.ProcessUploadAsync(content, settings.DownscaleOriginalsAbovePixels, cancellationToken);
        }
        catch (MediaProcessingException ex)
        {
            return Reject(result, length, ex.Message);
        }

        var item = await StoreNewItemAsync(fileName, new StoredOriginal(image.Content, image.ContentType, image.Extension, image.Hash),
            image, operations: null, altText: string.Empty, caption: null, cancellationToken);
        await renditionWriter.WriteAsync(item, image.Content, cancellationToken);

        var duplicates = await Project(dbContext.MediaItems.AsNoTracking()
                .Where(m => m.ContentHash == image.Hash && m.Id != item.Id)
                .OrderBy(m => m.Id))
            .ToListAsync(cancellationToken);

        MediaLog.MediaUploaded(logger, item.Id, item.PublicId, item.FileName, item.ContentType, item.Width, item.Height, item.SizeBytes);

        result.Item = (await GetMediaItemAsync(item.Id, cancellationToken))!;
        result.Duplicates = [.. duplicates.Select(ToDto)];
        return result;
    }

    /// <summary>Opens the stored original of an item for the image editor, or <see langword="null"/>.</summary>
    public async Task<(Stream Content, string ContentType)?> OpenOriginalAsync(int id, CancellationToken cancellationToken)
    {
        var item = await dbContext.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.OriginalStorageKey, m.ContentType })
            .SingleOrDefaultAsync(cancellationToken);

        return item is not null && await storage.OpenReadAsync(item.OriginalStorageKey, cancellationToken) is { } stream
            ? (stream, item.ContentType)
            : null;
    }

    /// <summary>
    /// Writes the files under a new public id and inserts the item's row, retrying with another id in the
    /// (astronomically unlikely) event that the id is taken. The files are removed again if the row can't be saved.
    /// </summary>
    /// <param name="fileName">The name to derive the URL file name from.</param>
    /// <param name="original">The original to keep for later edits and reverts.</param>
    /// <param name="current">The current version: the original itself for an upload, the edited copy for "Save as copy".</param>
    /// <param name="operations">The edits that turned the original into <paramref name="current"/>, if any.</param>
    /// <param name="altText">Alt text of the new item.</param>
    /// <param name="caption">Caption of the new item.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    private async Task<MediaItem> StoreNewItemAsync(string fileName, StoredOriginal original, ProcessedImage current,
        MediaEditOperations? operations, string altText, string? caption, CancellationToken cancellationToken)
    {
        var edited = operations is { IsIdentity: false };
        for (var attempt = 1; ; attempt++)
        {
            var publicId = MediaFileNames.NewPublicId();
            var originalKey = MediaStorageKeys.Original(publicId, original.Extension);
            var currentKey = edited ? MediaStorageKeys.Version(publicId, 1, current.Extension) : originalKey;
            var item = new MediaItem
            {
                PublicId = publicId,
                FileName = MediaFileNames.FromUpload(fileName, current.Extension),
                OriginalStorageKey = originalKey,
                CurrentStorageKey = currentKey,
                EditOperationsJson = edited ? JsonSerializer.Serialize(operations, OperationsJsonOptions) : null,
                ContentType = current.ContentType,
                Width = current.Width,
                Height = current.Height,
                SizeBytes = edited ? current.SizeBytes : original.Content.LongLength,
                AltText = altText,
                Caption = caption,
                ContentHash = original.Hash,
                Version = 1
            };

            await using (var content = new MemoryStream(original.Content, writable: false))
            {
                await storage.SaveAsync(originalKey, content, original.ContentType, cancellationToken);
            }

            if (edited)
            {
                await using var content = current.OpenRead();
                await storage.SaveAsync(currentKey, content, current.ContentType, cancellationToken);
            }

            dbContext.MediaItems.Add(item);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return item;
            }
            catch (Exception ex)
            {
                dbContext.ChangeTracker.Clear();
                await storage.DeletePrefixAsync(MediaStorageKeys.ItemPrefix(publicId), CancellationToken.None);

                if (ex is not DbUpdateException update || !update.IsUniqueViolation() || attempt >= MaxPublicIdAttempts)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>Records and logs a rejected file.</summary>
    private MediaUploadResult Reject(MediaUploadResult result, long length, string reason)
    {
        MediaLog.MediaUploadRejected(logger, result.FileName, length, reason);
        result.Error = reason;
        return result;
    }

    /// <summary>Posts, including those in the trash, for usage checks.</summary>
    private IQueryable<Post> UsingPosts()
    {
        return dbContext.Posts.IgnoreQueryFilters([QueryFilters.SoftDelete]);
    }

    /// <summary>The posts that use an item in their content, as their cover or as their social image.</summary>
    private async Task<List<MediaUsageDto>> LoadUsageAsync(int mediaId, CancellationToken cancellationToken)
    {
        return await UsingPosts()
            .AsNoTracking()
            .Where(p => p.CoverMediaId == mediaId || p.SocialImageMediaId == mediaId || p.PostMedia.Any(pm => pm.MediaItemId == mediaId))
            .OrderBy(p => p.Title)
            .Select(p => new MediaUsageDto { PostId = p.Id, Title = p.Title, IsInTrash = p.IsDeleted })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Ids of the posts that use an item.</summary>
    private async Task<List<int>> LoadUsingPostIdsAsync(int mediaId, CancellationToken cancellationToken)
    {
        return await UsingPosts()
            .Where(p => p.CoverMediaId == mediaId || p.SocialImageMediaId == mediaId || p.PostMedia.Any(pm => pm.MediaItemId == mediaId))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
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
            logger.LogWarning(ex, "Deleting the old media file {StorageKey} failed; it is no longer used.", key);
        }
    }

    /// <summary>The columns a <see cref="MediaItemDto"/> needs, with the usage count computed in the database.</summary>
    private IQueryable<MediaRow> Project(IQueryable<MediaItem> items)
    {
        var posts = UsingPosts();
        return items.Select(m => new MediaRow(
            m.Id, m.PublicId, m.FileName, m.ContentType, m.Width, m.Height, m.SizeBytes, m.AltText, m.Caption,
            m.Version, m.CreatedOn, m.EditOperationsJson,
            posts.Count(p => p.CoverMediaId == m.Id || p.SocialImageMediaId == m.Id || p.PostMedia.Any(pm => pm.MediaItemId == m.Id)),
            m.Renditions.Where(r => r.Format == MediaRenditionWriter.WebpFormat).OrderBy(r => r.Width).Select(r => r.Width).ToList()));
    }

    private static MediaItemDto ToDto(MediaRow row)
    {
        return new MediaItemDto
        {
            Id = row.Id,
            PublicId = row.PublicId,
            FileName = row.FileName,
            Url = MediaPaths.Versioned(row.PublicId, row.FileName, row.Version),
            Path = MediaPaths.Item(row.PublicId, row.FileName),
            ContentType = row.ContentType,
            Width = row.Width,
            Height = row.Height,
            SizeBytes = row.SizeBytes,
            AltText = row.AltText,
            Caption = row.Caption,
            Version = row.Version,
            CreatedOn = row.CreatedOn,
            EditOperations = row.EditOperationsJson is null
                ? null
                : JsonSerializer.Deserialize<MediaEditOperations>(row.EditOperationsJson, OperationsJsonOptions),
            UsageCount = row.UsageCount,
            RenditionWidths = row.RenditionWidths
        };
    }

    /// <summary>A media item as read for the admin area.</summary>
    private sealed record MediaRow(int Id, string PublicId, string FileName, string ContentType, int Width, int Height,
        long SizeBytes, string AltText, string? Caption, int Version, DateTimeOffset? CreatedOn, string? EditOperationsJson,
        int UsageCount, List<int> RenditionWidths);

    /// <summary>An original about to be stored for a new item.</summary>
    /// <param name="Content">Its bytes.</param>
    /// <param name="ContentType">Its media type.</param>
    /// <param name="Extension">Its file extension without the dot.</param>
    /// <param name="Hash">Lowercase hex SHA-256 of <paramref name="Content"/>.</param>
    private sealed record StoredOriginal(byte[] Content, string ContentType, string Extension, string Hash);
}