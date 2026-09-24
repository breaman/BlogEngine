using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Comments;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Server implementation of <see cref="IDashboardService"/> (design 4.5 O1, T3.10): post counts by state, the pending
/// comment count and the latest posts and comments, straight from the database so they are always current.
/// </summary>
public sealed class ServerDashboardService(
    ApplicationDbContext dbContext,
    IPostAdminService postAdminService,
    TimeProvider timeProvider) : IDashboardService
{
    /// <summary>How many recent posts and comments the activity lists show.</summary>
    public const int RecentCount = 5;

    /// <inheritdoc />
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var posts = dbContext.Posts.AsNoTracking();

        var summary = new DashboardSummaryDto
        {
            DraftCount = await posts.CountAsync(p => p.Status == PostStatus.Draft, cancellationToken),
            PublishedCount = await posts.CountAsync(p => p.Status == PostStatus.Published && p.PublishedOn <= now, cancellationToken),
            ScheduledCount = await posts.CountAsync(p => p.Status == PostStatus.Published && p.PublishedOn > now, cancellationToken),
            PendingCommentCount = await dbContext.Comments.CountAsync(c => c.Status == CommentStatus.Pending, cancellationToken)
        };

        // Recently changed posts, exactly as the posts list shows them.
        var recentPosts = await postAdminService.GetPostsAsync(new PostListQuery { PageSize = RecentCount }, cancellationToken);
        summary.RecentPosts = [.. recentPosts.Items];

        var recentComments = await ServerCommentModerationService.Project(dbContext.Comments.AsNoTracking()
                .OrderByDescending(c => c.CreatedOn)
                .ThenByDescending(c => c.Id)
                .Take(RecentCount))
            .ToListAsync(cancellationToken);
        summary.RecentComments = [.. recentComments.Select(ServerCommentModerationService.WithPostPath)];

        return summary;
    }
}
