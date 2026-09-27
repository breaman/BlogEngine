using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Hybrid;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Evicts cached public content when the data behind it changes (design 11), so readers see a publish,
/// update, unpublish, delete or settings change on their next request.
/// </summary>
/// <remarks>
/// <para>
/// Two caches are involved: <see cref="HybridCache"/> holds the query results of <see cref="PublicPostQueries"/>,
/// <see cref="PublicCommentQueries"/> and <see cref="PublicPageQueries"/> (and the settings), and the output cache holds
/// whole responses for feeds, the sitemap and <c>robots.txt</c>. Both are evicted by the tags in
/// <see cref="PublicCacheTags"/>.
/// </para>
/// <para>
/// <b>Why a generation as well as tags.</b> Tag eviction alone has a race: a cache load that read the database
/// just before a change committed can finish just after the eviction, and <see cref="HybridCache"/> then stores
/// its stale result as fresh (requests arriving meanwhile even join that in-flight load). So every eviction also
/// bumps <see cref="PostsGeneration"/>, which <see cref="PublicPostQueries"/> puts in its cache keys: a request
/// that starts after the eviction uses a new key and a new load that reads the committed data. Entries under old
/// keys are never read again and expire on their own.
/// </para>
/// <para>
/// Call it only after the change has been committed. Eviction ignores the caller's cancellation token: once the
/// change is saved, a request aborted by the browser must not leave stale pages behind.
/// </para>
/// </remarks>
public sealed class CacheInvalidator(HybridCache cache, IOutputCacheStore outputCache, ILogger<CacheInvalidator> logger)
{
    private long postsGeneration;
    private long commentsGeneration;
    private long pagesGeneration;

    /// <summary>Changes after every eviction of post-derived content; part of the public cache keys.</summary>
    public long PostsGeneration => Interlocked.Read(ref postsGeneration);

    /// <summary>Changes after every eviction of comments; part of the cache keys of <see cref="PublicCommentQueries"/>.</summary>
    public long CommentsGeneration => Interlocked.Read(ref commentsGeneration);

    /// <summary>Changes after every eviction of standalone pages; part of the cache keys of <see cref="PublicPageQueries"/>.</summary>
    public long PagesGeneration => Interlocked.Read(ref pagesGeneration);

    /// <summary>
    /// Evicts everything that could show the post: its own entries and every list, archive, tag page, feed
    /// and sitemap entry, since a change to one post can move it in or out of all of them.
    /// </summary>
    /// <param name="postId">The post that was created, edited, published, unpublished or deleted.</param>
    public async Task PostChangedAsync(int postId)
    {
        Interlocked.Increment(ref postsGeneration);
        await cache.RemoveByTagAsync([PublicCacheTags.Posts, PublicCacheTags.Post(postId)], CancellationToken.None);
        await outputCache.EvictByTagAsync(PublicCacheTags.Posts, CancellationToken.None);

        logger.LogDebug("Evicted the public caches after post {PostId} changed.", postId);
    }

    /// <summary>
    /// Evicts every post-derived entry after tags were renamed or merged (design 6.4, O3): post pages, lists and feeds show
    /// tag names and link to tag pages, and the tag index and tag pages are built from the same snapshot.
    /// </summary>
    public async Task TagsChangedAsync()
    {
        Interlocked.Increment(ref postsGeneration);
        await cache.RemoveByTagAsync(PublicCacheTags.Posts, CancellationToken.None);
        await outputCache.EvictByTagAsync(PublicCacheTags.Posts, CancellationToken.None);

        logger.LogDebug("Evicted the public caches after tags changed.");
    }

    /// <summary>
    /// Evicts the approved comments of the posts, after a comment was approved, or one that was approved was
    /// rejected, flagged or deleted (design 11).
    /// </summary>
    /// <param name="postIds">The posts whose comments changed.</param>
    public async Task CommentsChangedAsync(IEnumerable<int> postIds)
    {
        ArgumentNullException.ThrowIfNull(postIds);

        var tags = postIds.Distinct().Select(PublicCacheTags.Comments).ToList();
        if (tags.Count == 0)
        {
            return;
        }

        Interlocked.Increment(ref commentsGeneration);
        await cache.RemoveByTagAsync(tags, CancellationToken.None);

        logger.LogDebug("Evicted the cached comments of {PostCount} posts.", tags.Count);
    }

    /// <summary>
    /// Evicts the standalone pages (design 6.7, A17) after one was published, edited, unpublished or deleted: the page
    /// snapshot (navigation links on every public page) and page contents, plus the sitemap, which lists the pages.
    /// </summary>
    /// <param name="pageId">The page that changed.</param>
    public async Task PagesChangedAsync(int pageId)
    {
        Interlocked.Increment(ref pagesGeneration);
        await cache.RemoveByTagAsync(PublicCacheTags.Pages, CancellationToken.None);
        await outputCache.EvictByTagAsync(PublicCacheTags.Posts, CancellationToken.None);

        logger.LogDebug("Evicted the cached pages after page {PageId} changed.", pageId);
    }

    /// <summary>
    /// Evicts the cached settings and everything the settings affect: all post-derived entries (the time zone
    /// changes URL dates, the feed mode changes feeds) and settings-only responses such as <c>robots.txt</c>.
    /// </summary>
    /// <param name="settingsCacheKey">Key of the cached settings entry.</param>
    public async Task SettingsChangedAsync(string settingsCacheKey)
    {
        Interlocked.Increment(ref postsGeneration);
        await cache.RemoveAsync(settingsCacheKey, CancellationToken.None);
        await cache.RemoveByTagAsync([PublicCacheTags.Posts, PublicCacheTags.Settings], CancellationToken.None);
        await outputCache.EvictByTagAsync(PublicCacheTags.Posts, CancellationToken.None);
        await outputCache.EvictByTagAsync(PublicCacheTags.Settings, CancellationToken.None);

        logger.LogDebug("Evicted the public caches after the settings changed.");
    }
}