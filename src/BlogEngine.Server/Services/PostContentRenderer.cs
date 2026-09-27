using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Media;
using BlogEngine.Server.Services.Public;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Markdown;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Renders a post's Markdown to the sanitized HTML that is stored in <see cref="Post.ContentHtml"/> (design 10.1),
/// resolving media library images against the database (design 9.4).
/// </summary>
/// <remarks>
/// Used by the post save pipeline, and by the media library to refresh stored HTML when an image is edited or
/// deleted, since the HTML carries the image's size and <c>?v=</c> version (design 9.2, 9.6).
/// </remarks>
public sealed class PostContentRenderer(
    ApplicationDbContext dbContext,
    PostHtmlSanitizer sanitizer,
    CacheInvalidator cacheInvalidator)
{
    /// <summary>Renders <paramref name="markdown"/> and reports the library items it uses.</summary>
    public async Task<RenderedPostContent> RenderAsync(string markdown, CancellationToken cancellationToken)
    {
        var media = await MediaLookupQuery.LoadAsync(dbContext, MediaReferenceScanner.FindPublicIds(markdown), cancellationToken);
        var rendered = BlogMarkdownPipeline.Default.RenderPost(markdown, media);

        return new RenderedPostContent(
            sanitizer.Sanitize(rendered.Html),
            rendered.ContainsCodeBlocks,
            [.. media.Items.Select(i => i.Id)]);
    }

    /// <summary>
    /// Re-renders the stored HTML of the given posts (including posts in the trash) from their Markdown, then evicts
    /// the public caches of the published ones.
    /// </summary>
    /// <remarks>
    /// Written with <c>ExecuteUpdate</c> so the posts' modified stamps stay as they are: this is not an edit by the
    /// author, and moving the stamp would hide a published post's pending autosave. The row version still changes,
    /// so an editor open on one of these posts reports a conflict on its next save and offers to reload.
    /// </remarks>
    /// <returns>The number of posts re-rendered.</returns>
    public async Task<int> RerenderAsync(IReadOnlyCollection<int> postIds, CancellationToken cancellationToken)
    {
        if (postIds.Count == 0)
        {
            return 0;
        }

        var posts = await dbContext.Posts
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .AsNoTracking()
            .Where(p => postIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ContentMarkdown, p.Status })
            .ToListAsync(cancellationToken);

        foreach (var post in posts)
        {
            var rendered = await RenderAsync(post.ContentMarkdown, cancellationToken);
            await dbContext.Posts
                .IgnoreQueryFilters([QueryFilters.SoftDelete])
                .Where(p => p.Id == post.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.ContentHtml, rendered.Html)
                    .SetProperty(p => p.HasCodeBlocks, rendered.ContainsCodeBlocks), cancellationToken);

            if (post.Status == PostStatus.Published)
            {
                await cacheInvalidator.PostChangedAsync(post.Id);
            }
        }

        return posts.Count;
    }
}

/// <summary>A post's rendered content.</summary>
/// <param name="Html">Sanitized HTML.</param>
/// <param name="ContainsCodeBlocks">Whether the post needs the code highlighting script (design 10.3).</param>
/// <param name="MediaItemIds">Ids of the library items the content uses, for <see cref="PostMedia"/>.</param>
public sealed record RenderedPostContent(string Html, bool ContainsCodeBlocks, IReadOnlyList<int> MediaItemIds);