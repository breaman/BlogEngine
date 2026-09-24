using System.Globalization;

using BlogEngine.Data.Models;
using BlogEngine.Data.Queries;
using BlogEngine.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// The read side of the public site (design 7.1, 11, T1.17): post lists, archives, tag counts, single posts,
/// feed items and sitemap entries. Every query goes through the <see cref="PostQueryExtensions.VisibleToPublic"/>
/// rule, and results are cached in <see cref="HybridCache"/> until <see cref="CacheInvalidator"/> evicts them.
/// </summary>
/// <remarks>
/// <para>
/// <b>One snapshot for the lists.</b> Rather than caching every list page, archive period and tag page under
/// its own key, the summaries of all visible posts and the tag counts are cached as one
/// <see cref="PublicPostIndex"/> (tag <see cref="PublicCacheTags.Posts"/>), and pages are sliced from it in
/// memory. At personal-blog scale (hundreds of posts, a few hundred KB) this costs two queries per
/// invalidation, keeps every list consistent with the others, and means that crawling arbitrary archive dates,
/// page numbers or slugs can't fill the cache with one entry per URL.
/// </para>
/// <para>
/// <b>Content per post.</b> The rendered HTML is only loaded for the post being viewed, cached per post id
/// with the tags <see cref="PublicCacheTags.Posts"/> and <see cref="PublicCacheTags.Post"/>. Only slugs found in
/// the snapshot reach this cache, so its keys are bounded by the number of posts. Feed items are cached the same
/// way, per feed, with <see cref="PublicCacheTags.Tag"/> on a tag's feed.
/// </para>
/// <para>
/// <b>Keys.</b> Every key includes <see cref="CacheInvalidator.PostsGeneration"/>, so a request that starts after
/// a change can't be handed a result loaded before it (see <see cref="CacheInvalidator"/>).
/// </para>
/// <para>
/// <b>Time.</b> Visibility is evaluated when a cache entry is built. Scheduled posts (T4.1) become visible when
/// <see cref="ScheduledPublishWatcher"/> evicts <see cref="PublicCacheTags.Posts"/>; should it fail, the expiration
/// below is the upper bound on how stale an entry can be.
/// </para>
/// <para>
/// Cache misses load through their own scope, like <see cref="ServerSettingsService"/>: during static SSR the
/// layout and the page initialize concurrently, and a load that <see cref="HybridCache"/> shares between
/// concurrent requests must not depend on one request's <see cref="ApplicationDbContext"/>. That also makes
/// this class safe to register as a singleton.
/// </para>
/// </remarks>
public sealed class PublicPostQueries(HybridCache cache, CacheInvalidator invalidator, IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider)
{
    /// <summary>Posts in the site and tag feeds (design 16).</summary>
    public const int FeedItemCount = 20;

    /// <summary>Safety net for entries nothing evicts, such as a post whose scheduled time has passed.</summary>
    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(10)
    };

    private static readonly string[] IndexTags = [PublicCacheTags.Posts];

    /// <summary>The snapshot of visible posts and tags that the list queries are computed from.</summary>
    public async Task<PublicPostIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(Key("index"), LoadIndexAsync, EntryOptions, IndexTags, cancellationToken);
    }

    /// <summary>A page of all visible posts, newest first (<c>/posts</c>).</summary>
    public async Task<PublicPostPage> GetLatestAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var index = await GetIndexAsync(cancellationToken);
        return PublicPostPage.Create(index.Posts, page, pageSize);
    }

    /// <summary>Up to <paramref name="count"/> featured posts, newest first, for the home page.</summary>
    public async Task<IReadOnlyList<PublicPostSummary>> GetFeaturedAsync(int count, CancellationToken cancellationToken = default)
    {
        var index = await GetIndexAsync(cancellationToken);
        return [.. index.Posts.Where(p => p.IsFeatured).Take(count)];
    }

    /// <summary>A page of the posts published in <paramref name="period"/> (local date), newest first.</summary>
    public async Task<PublicPostPage> GetArchiveAsync(ArchivePeriod period, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        var index = await GetIndexAsync(cancellationToken);
        return PublicPostPage.Create([.. index.Posts.Where(p => period.Contains(p.PublishedDateLocal))], page, pageSize);
    }

    /// <summary>Tags with at least one visible post, with their counts, ordered by name.</summary>
    public async Task<IReadOnlyList<PublicTag>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        return (await GetIndexAsync(cancellationToken)).Tags;
    }

    /// <summary>The tag with this slug, or <see langword="null"/> if it doesn't exist or has no visible posts.</summary>
    public async Task<PublicTag?> GetTagAsync(string slug, CancellationToken cancellationToken = default)
    {
        return (await GetIndexAsync(cancellationToken)).FindTag(slug);
    }

    /// <summary>A page of the visible posts with the tag, newest first.</summary>
    public async Task<PublicPostPage> GetTaggedAsync(int tagId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var index = await GetIndexAsync(cancellationToken);
        return PublicPostPage.Create([.. index.Posts.Where(p => p.Tags.Any(t => t.Id == tagId))], page, pageSize);
    }

    /// <summary>
    /// The visible post with this slug and its rendered content, or <see langword="null"/>. The caller compares
    /// the post's <see cref="PublicPostSummary.Path"/> with the request to redirect wrong dates (design 7.1).
    /// </summary>
    public async Task<PublicPostContent?> GetPostAsync(string slug, CancellationToken cancellationToken = default)
    {
        var summary = (await GetIndexAsync(cancellationToken)).FindPost(slug);
        if (summary is null)
        {
            return null;
        }

        return await cache.GetOrCreateAsync(Key("post", summary.Id), (Queries: this, summary.Id),
            static (state, ct) => state.Queries.LoadContentAsync(state.Id, ct),
            EntryOptions, [PublicCacheTags.Posts, PublicCacheTags.Post(summary.Id)], cancellationToken);
    }

    /// <summary>
    /// The newest <see cref="FeedItemCount"/> visible posts with content, for the site feeds, or for one tag's
    /// feed when <paramref name="tagId"/> is set.
    /// </summary>
    public async Task<IReadOnlyList<PublicPostContent>> GetFeedAsync(int? tagId, CancellationToken cancellationToken = default)
    {
        string[] tags = tagId is { } id ? [PublicCacheTags.Posts, PublicCacheTags.Tag(id)] : [PublicCacheTags.Posts];
        var feed = await cache.GetOrCreateAsync(Key(tagId is null ? "feed" : "feed:tag", tagId), (Queries: this, TagId: tagId),
            static (state, ct) => state.Queries.LoadFeedAsync(state.TagId, ct),
            EntryOptions, tags, cancellationToken);

        return feed.Posts;
    }

    /// <summary>
    /// The cache key of entry <paramref name="name"/> (for the entity <paramref name="id"/>, if any) in the current
    /// generation of post-derived entries, such as <c>public:7:post:42</c>.
    /// </summary>
    private string Key(string name, int? id = null)
    {
        return id is { } value
            ? string.Create(CultureInfo.InvariantCulture, $"public:{invalidator.PostsGeneration}:{name}:{value}")
            : string.Create(CultureInfo.InvariantCulture, $"public:{invalidator.PostsGeneration}:{name}");
    }

    /// <summary>Loads the summaries of all visible posts and the visible post count of every tag.</summary>
    private async ValueTask<PublicPostIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var visible = dbContext.Posts.AsNoTracking().VisibleToPublic(timeProvider);

        var rows = await visible
            .OrderByDescending(p => p.PublishedOn)
            .ThenByDescending(p => p.Id)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
                p.Summary,
                PublishedOn = p.PublishedOn!.Value,
                p.PublishedDateLocal,
                p.LastUpdatedOn,
                p.ReadingMinutes,
                p.IsFeatured,
                Tags = p.Tags.OrderBy(t => t.Name).Select(t => new { t.Id, t.Name, t.Slug }).ToList()
            })
            .ToListAsync(cancellationToken);

        // The visible-post count per tag, counted by the database through the same visibility rule.
        var tagRows = await dbContext.Tags
            .AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Slug,
                t.Description,
                PostCount = visible.Count(p => p.Tags.Any(pt => pt.Id == t.Id))
            })
            .Where(t => t.PostCount > 0)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var posts = rows
            // A published post always has a local date; skip a row that somehow lacks one rather than fail every page.
            .Where(r => r.PublishedDateLocal is not null && r.Slug.Length > 0)
            .Select(r => new PublicPostSummary(r.Id, r.Title, r.Slug, r.Summary, r.PublishedOn, r.PublishedDateLocal!.Value,
                r.LastUpdatedOn, r.ReadingMinutes, r.IsFeatured,
                [.. r.Tags.Select(t => new PublicTagLink(t.Id, t.Name, t.Slug))]))
            .ToArray();
        var tags = tagRows.Select(t => new PublicTag(t.Id, t.Name, t.Slug, t.Description, t.PostCount)).ToArray();

        return new PublicPostIndex(posts, tags);
    }

    /// <summary>Loads a visible post's rendered content.</summary>
    private async ValueTask<PublicPostContent?> LoadContentAsync(int postId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var posts = await LoadContentsAsync(
            dbContext.Posts.AsNoTracking().VisibleToPublic(timeProvider).Where(p => p.Id == postId), cancellationToken);

        return posts.FirstOrDefault();
    }

    /// <summary>Loads the newest visible posts with content, optionally only those with a tag.</summary>
    private async ValueTask<PublicFeedItems> LoadFeedAsync(int? tagId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var visible = dbContext.Posts.AsNoTracking().VisibleToPublic(timeProvider);
        if (tagId is { } id)
        {
            visible = visible.Where(p => p.Tags.Any(t => t.Id == id));
        }

        return new PublicFeedItems(await LoadContentsAsync(
            visible.OrderByDescending(p => p.PublishedOn).ThenByDescending(p => p.Id).Take(FeedItemCount), cancellationToken));
    }

    /// <summary>Projects already filtered and ordered posts into <see cref="PublicPostContent"/>.</summary>
    private static async Task<IReadOnlyList<PublicPostContent>> LoadContentsAsync(IQueryable<Post> posts,
        CancellationToken cancellationToken)
    {
        var rows = await posts
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
                p.Summary,
                PublishedOn = p.PublishedOn!.Value,
                p.PublishedDateLocal,
                p.LastUpdatedOn,
                p.ReadingMinutes,
                p.IsFeatured,
                Tags = p.Tags.OrderBy(t => t.Name).Select(t => new { t.Id, t.Name, t.Slug }).ToList(),
                p.ContentHtml,
                p.HasCodeBlocks,
                p.MetaTitle,
                p.MetaDescription,
                p.AllowComments,
                p.CommentsCloseOn,
                Cover = p.CoverMedia == null
                    ? null
                    : new { p.CoverMedia.PublicId, p.CoverMedia.FileName, p.CoverMedia.Version, p.CoverMedia.Width, p.CoverMedia.Height, p.CoverMedia.AltText },
                Social = p.SocialImageMedia == null
                    ? null
                    : new { p.SocialImageMedia.PublicId, p.SocialImageMedia.FileName, p.SocialImageMedia.Version, p.SocialImageMedia.Width, p.SocialImageMedia.Height, p.SocialImageMedia.AltText }
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .Where(r => r.PublishedDateLocal is not null && r.Slug.Length > 0)
                .Select(r => new PublicPostContent(
                    new PublicPostSummary(r.Id, r.Title, r.Slug, r.Summary, r.PublishedOn, r.PublishedDateLocal!.Value,
                        r.LastUpdatedOn, r.ReadingMinutes, r.IsFeatured,
                        [.. r.Tags.Select(t => new PublicTagLink(t.Id, t.Name, t.Slug))]),
                    r.ContentHtml, r.HasCodeBlocks, r.MetaTitle, r.MetaDescription, r.AllowComments, r.CommentsCloseOn,
                    r.Cover is { } cover
                        ? new PublicImage(MediaPaths.Versioned(cover.PublicId, cover.FileName, cover.Version), cover.Width, cover.Height, cover.AltText)
                        : null,
                    r.Social is { } social
                        ? new PublicImage(MediaPaths.Versioned(social.PublicId, social.FileName, social.Version), social.Width, social.Height, social.AltText)
                        : null))
        ];
    }
}
