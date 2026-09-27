using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Keeps post URL dates in step with the blog's time zone (design 7.1, Q10, T1.16).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Post.PublishedDateLocal"/> is the publish instant seen in the configured time zone, and it is part
/// of every post URL. When the time zone setting changes, the stored dates are recomputed so URLs and archives
/// follow the new zone, and each published post whose URL changed gets a 301 from its old URL.
/// </para>
/// <para>
/// Changes are only added to the context, so the caller's <c>SaveChanges</c> commits them together with the
/// new setting: the dates, the redirects and the time zone change as one transaction.
/// </para>
/// </remarks>
internal static class PostLocalDateMaintenance
{
    /// <summary>
    /// Recomputes the local publish date of every post that has one (trashed posts included, so restoring them
    /// later gives the right URL) and adds redirects for published posts whose URL moved.
    /// </summary>
    /// <param name="dbContext">The context the caller will save.</param>
    /// <param name="timeZoneId">The new time zone; must be known on this host (the settings validator checks it).</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>How many posts got a new date.</returns>
    public static async Task<int> RecomputeAsync(ApplicationDbContext dbContext, string timeZoneId,
        CancellationToken cancellationToken)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        // Read only what's needed to find the posts that move, so a large blog doesn't load every post's content.
        var dated = await dbContext.Posts
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .AsNoTracking()
            .Where(p => p.PublishedOn != null)
            .Select(p => new { p.Id, PublishedOn = p.PublishedOn!.Value, p.PublishedDateLocal })
            .ToListAsync(cancellationToken);

        var newDates = dated
            .Select(p => (p.Id, Date: BlogTimeZone.ToLocalDate(p.PublishedOn, timeZone), p.PublishedDateLocal))
            .Where(p => p.Date != p.PublishedDateLocal)
            .ToDictionary(p => p.Id, p => p.Date);

        if (newDates.Count == 0)
        {
            return 0;
        }

        var ids = newDates.Keys.ToList();
        var posts = await dbContext.Posts
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        foreach (var post in posts)
        {
            var oldDate = post.PublishedDateLocal;
            post.PublishedDateLocal = newDates[post.Id];

            // Only URLs that were public can have links pointing at them.
            if (post is { Status: PostStatus.Published, IsDeleted: false } && oldDate is { } date && post.Slug.Length > 0)
            {
                await RedirectWriter.AddAsync(dbContext, PostPaths.Post(date, post.Slug),
                    PostPaths.Post(post.PublishedDateLocal.Value, post.Slug), cancellationToken);
            }
        }

        return posts.Count;
    }
}