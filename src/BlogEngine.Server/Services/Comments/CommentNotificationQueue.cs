using System.Threading.Channels;

namespace BlogEngine.Server.Services.Comments;

/// <summary>
/// Hands new pending comments from the comment form to <see cref="CommentNotificationSender"/> (design 8.4, C9), so a
/// slow or unreachable mail server never delays or fails a reader's submission.
/// </summary>
/// <remarks>
/// In memory and bounded: notifications queued when the server stops are lost, which only means the author finds the
/// comment in the moderation queue instead of their inbox. When the queue is full (the mail server has been down during
/// a flood of comments) new notifications are dropped rather than holding memory.
/// </remarks>
public sealed class CommentNotificationQueue
{
    /// <summary>Most notifications waiting at once.</summary>
    public const int Capacity = 100;

    private readonly Channel<CommentNotification> channel = Channel.CreateBounded<CommentNotification>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Queues a notification; <see langword="false"/> when the queue is full and it was dropped.</summary>
    public bool TryEnqueue(CommentNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return channel.Writer.TryWrite(notification);
    }

    /// <summary>The queued notifications, as they arrive.</summary>
    public IAsyncEnumerable<CommentNotification> ReadAllAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.ReadAllAsync(cancellationToken);
    }
}

/// <summary>A new comment the author should hear about.</summary>
/// <param name="CommentId">The comment.</param>
/// <param name="SiteRoot">The site's absolute root URL at the time, for the links in the email; none when unknown.</param>
public sealed record CommentNotification(int CommentId, Uri? SiteRoot);