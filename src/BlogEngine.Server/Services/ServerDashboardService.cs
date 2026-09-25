using System.Globalization;

using BlogEngine.Data.Interfaces;
using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Comments;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Server implementation of <see cref="IDashboardService"/> (design 4.5 O1, 12.1, T3.10, T4.1, T4.26): post counts by state,
/// the pending comment count, the next scheduled posts and the latest posts and comments, straight from the database so
/// they are always current, and whether the signed-in admin still needs a passkey or two-factor authentication.
/// </summary>
public sealed class ServerDashboardService(
    ApplicationDbContext dbContext,
    IPostAdminService postAdminService,
    UserManager<User> userManager,
    IUserService userService,
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

        if (summary.ScheduledCount > 0)
        {
            var scheduled = await postAdminService.GetPostsAsync(
                new PostListQuery { Status = PostListStatus.Scheduled, PageSize = RecentCount }, cancellationToken);
            summary.ScheduledPosts = [.. scheduled.Items];
        }

        var recentComments = await ServerCommentModerationService.Project(dbContext.Comments.AsNoTracking()
                .OrderByDescending(c => c.CreatedOn)
                .ThenByDescending(c => c.Id)
                .Take(RecentCount))
            .ToListAsync(cancellationToken);
        summary.RecentComments = [.. recentComments.Select(ServerCommentModerationService.WithPostPath)];
        summary.ShowSecurityNudge = await NeedsSecurityNudgeAsync();

        return summary;
    }

    /// <summary>
    /// Whether the signed-in user has neither a passkey nor two-factor authentication (design 12.1). An account that can't
    /// be found (no signed-in user) gets no nudge: there is nobody to act on it.
    /// </summary>
    private async Task<bool> NeedsSecurityNudgeAsync()
    {
        if (userService.UserId <= 0
            || await userManager.FindByIdAsync(userService.UserId.ToString(CultureInfo.InvariantCulture)) is not { } user)
        {
            return false;
        }

        return !await userManager.GetTwoFactorEnabledAsync(user) && (await userManager.GetPasskeysAsync(user)).Count == 0;
    }
}
