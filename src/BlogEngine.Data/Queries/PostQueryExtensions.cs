using BlogEngine.Data.Models;
using BlogEngine.Shared.Enums;

namespace BlogEngine.Data.Queries;

/// <summary>
/// Query extensions for <see cref="Post"/>.
/// </summary>
public static class PostQueryExtensions
{
    /// <summary>
    /// Restricts posts to those readers may see: published, with a publish time that has passed, and not
    /// in the trash (design 6.3). Every public query must go through this rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scheduling needs no background job: a post with a future <see cref="Post.PublishedOn"/> is simply
    /// filtered out until its time passes.
    /// </para>
    /// <para>
    /// The trash check duplicates the global soft-delete query filter on purpose, so a query that calls
    /// <c>IgnoreQueryFilters()</c> for another reason still can't leak a trashed post.
    /// </para>
    /// <para>
    /// The current time is read once and sent as a parameter, which keeps the query plan cacheable and lets
    /// tests substitute a fake <see cref="TimeProvider"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var latest = await db.Posts
    ///     .VisibleToPublic(timeProvider)
    ///     .OrderByDescending(p => p.PublishedOn)
    ///     .Take(10)
    ///     .ToListAsync(cancellationToken);
    /// </code>
    /// </example>
    public static IQueryable<Post> VisibleToPublic(this IQueryable<Post> posts, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(posts);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.GetUtcNow();

        return posts.Where(p => p.Status == PostStatus.Published
            && p.PublishedOn != null
            && p.PublishedOn <= now
            && !p.IsDeleted);
    }
}