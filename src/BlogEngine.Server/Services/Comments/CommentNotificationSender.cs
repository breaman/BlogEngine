using BlogEngine.Data.Models;
using BlogEngine.Server.Services.Email;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Security;

using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Emails the blog's admins about each new comment waiting for moderation (design 8.4, C9), when the "email me on new
/// pending comments" setting is on. Runs in the background, fed by <see cref="CommentNotificationQueue"/>.
/// </summary>
/// <remarks>
/// A comment already moderated by the time its turn comes (the author was quicker than the mail) is skipped. A failure
/// is logged and the next notification is tried; nothing is retried, since the moderation queue and nav badge still
/// show the comment.
/// </remarks>
public sealed class CommentNotificationSender(
    CommentNotificationQueue queue,
    IServiceScopeFactory scopeFactory,
    IEmailTransport transport,
    ILogger<CommentNotificationSender> logger) : BackgroundService
{
    /// <summary>Sends the notifications as they are queued, until the application stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var notification in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendAsync(notification, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    CommentLog.CommentNotificationFailed(logger, ex, notification.CommentId, 0);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>Emails the admins about one comment, if it is still pending.</summary>
    /// <returns>Whether an email was handed to the transport.</returns>
    public async Task<bool> SendAsync(CommentNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var comment = await dbContext.Comments
            .AsNoTracking()
            .Where(c => c.Id == notification.CommentId && c.Status == CommentStatus.Pending)
            .Select(c => new CommentNotificationContent(c.Id, c.PostId, c.Post.Title, c.Post.PublishedDateLocal, c.Post.Slug,
                c.AuthorName, c.BodyHtml, c.BodyMarkdown, c.SpamScore, c.SpamReasons))
            .SingleOrDefaultAsync(cancellationToken);
        if (comment is null)
        {
            return false;
        }

        var recipients = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Email != null && dbContext.UserRoles.Any(ur => ur.UserId == u.Id
                && dbContext.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRoles.Admin)))
            .Select(u => u.Email!)
            .ToListAsync(cancellationToken);
        if (recipients.Count == 0)
        {
            return false;
        }

        try
        {
            await transport.SendAsync(CommentNotificationMessage.Create(comment, recipients, notification.SiteRoot), cancellationToken);
        }
        catch (EmailDeliveryException ex)
        {
            CommentLog.CommentNotificationFailed(logger, ex, comment.CommentId, comment.PostId);
            return false;
        }

        CommentLog.CommentNotificationSent(logger, comment.CommentId, comment.PostId, recipients.Count);
        return true;
    }
}

/// <summary>What the notification email says about a comment.</summary>
/// <param name="CommentId">The comment.</param>
/// <param name="PostId">Its post.</param>
/// <param name="PostTitle">The post's title.</param>
/// <param name="PublishedDateLocal">The post's URL date.</param>
/// <param name="Slug">The post's slug.</param>
/// <param name="AuthorName">Who wrote the comment.</param>
/// <param name="BodyHtml">The comment's sanitized HTML.</param>
/// <param name="BodyMarkdown">The comment as typed, for the plain-text part.</param>
/// <param name="SpamScore">The spam guard's score.</param>
/// <param name="SpamReasons">Why it scored, if it did.</param>
public sealed record CommentNotificationContent(int CommentId, int PostId, string PostTitle, DateOnly? PublishedDateLocal, string Slug,
    string AuthorName, string BodyHtml, string BodyMarkdown, int SpamScore, string? SpamReasons)
{
    /// <summary>The post's public path, when it has a URL date.</summary>
    public string? PostPath => PublishedDateLocal is { } date ? PostPaths.Post(date, Slug) : null;
}