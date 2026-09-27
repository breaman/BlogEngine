using BlogEngine.Shared.Enums;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Structured log events for comments (design 12.4, 18, T3.11). Each has a stable event id and name, so the events can
/// be found in Serilog or OpenTelemetry by <c>EventId.Name</c> and filtered on their properties. Commenter emails and
/// IP addresses are never logged; the comment id leads to them for anyone allowed to see them.
/// </summary>
internal static partial class CommentLog
{
    /// <summary>A comment was stored, with the spam guard's verdict.</summary>
    [LoggerMessage(EventId = 3001, EventName = "CommentSubmitted", Level = LogLevel.Information,
        Message = "Comment {CommentId} on post {PostId} submitted as {Status} with spam score {SpamScore}: {SpamReasons}")]
    public static partial void CommentSubmitted(ILogger logger, int commentId, int postId, CommentStatus status, int spamScore,
        string spamReasons);

    /// <summary>A submission failed the honeypot or time trap and was thrown away.</summary>
    [LoggerMessage(EventId = 3002, EventName = "CommentDiscarded", Level = LogLevel.Warning,
        Message = "Comment on post {PostId} discarded by the spam guard: {Reason}")]
    public static partial void CommentDiscarded(ILogger logger, int postId, string reason);

    /// <summary>A moderator changed a comment's status.</summary>
    [LoggerMessage(EventId = 3003, EventName = "CommentModerated", Level = LogLevel.Information,
        Message = "Comment {CommentId} on post {PostId} moderated from {PreviousStatus} to {Status} by user {ModeratorId}")]
    public static partial void CommentModerated(ILogger logger, int commentId, int postId, CommentStatus previousStatus,
        CommentStatus status, int moderatorId);

    /// <summary>A moderator deleted a comment permanently.</summary>
    [LoggerMessage(EventId = 3004, EventName = "CommentDeleted", Level = LogLevel.Information,
        Message = "Comment {CommentId} on post {PostId} ({Status}) deleted by user {ModeratorId}")]
    public static partial void CommentDeleted(ILogger logger, int commentId, int postId, CommentStatus status, int moderatorId);

    /// <summary>A moderator blocked a commenter.</summary>
    [LoggerMessage(EventId = 3005, EventName = "CommenterBlocked", Level = LogLevel.Information,
        Message = "Commenter of comment {CommentId} blocked by user {ModeratorId}: {BlocksAdded} blocks added, {CommentsMarkedSpam} comments marked as spam")]
    public static partial void CommenterBlocked(ILogger logger, int commentId, int moderatorId, int blocksAdded, int commentsMarkedSpam);

    /// <summary>Old spam was purged.</summary>
    [LoggerMessage(EventId = 3006, EventName = "SpamEmptied", Level = LogLevel.Information,
        Message = "{Count} spam comments older than {Cutoff} deleted by user {ModeratorId}")]
    public static partial void SpamEmptied(ILogger logger, int count, DateTimeOffset cutoff, int moderatorId);

    /// <summary>A commenter hit the rate limit.</summary>
    [LoggerMessage(EventId = 3007, EventName = "CommentRateLimited", Level = LogLevel.Warning,
        Message = "Comment post to {Path} rejected by the rate limiter")]
    public static partial void CommentRateLimited(ILogger logger, string path);

    /// <summary>The author published a reply or comment of their own.</summary>
    [LoggerMessage(EventId = 3008, EventName = "AuthorCommentPosted", Level = LogLevel.Information,
        Message = "Author comment {CommentId} on post {PostId} posted by user {AuthorId} in reply to {ParentCommentId}")]
    public static partial void AuthorCommentPosted(ILogger logger, int commentId, int postId, int authorId, int? parentCommentId);

    /// <summary>Emailing the author about a new pending comment failed.</summary>
    [LoggerMessage(EventId = 3009, EventName = "CommentNotificationFailed", Level = LogLevel.Error,
        Message = "The notification email for comment {CommentId} on post {PostId} couldn't be sent")]
    public static partial void CommentNotificationFailed(ILogger logger, Exception exception, int commentId, int postId);

    /// <summary>The author was emailed about a new pending comment.</summary>
    [LoggerMessage(EventId = 3010, EventName = "CommentNotificationSent", Level = LogLevel.Information,
        Message = "Notification email for comment {CommentId} on post {PostId} sent to {RecipientCount} recipients")]
    public static partial void CommentNotificationSent(ILogger logger, int commentId, int postId, int recipientCount);
}