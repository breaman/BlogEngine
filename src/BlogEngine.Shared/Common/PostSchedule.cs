using BlogEngine.Shared.Enums;

namespace BlogEngine.Shared.Common;

/// <summary>
/// The derived publishing states of a post (design 6.3, A9). "Scheduled" is not stored as a status of its own: it is
/// <see cref="PostStatus.Published"/> with a <c>PublishedOn</c> that is still in the future.
/// </summary>
/// <remarks>
/// Shared by the server (post lists, redirects) and the WebAssembly editor, so both classify a post the same way.
/// Always pass the current time from a <see cref="TimeProvider"/>, which tests can replace.
/// </remarks>
/// <example>
/// <code>
/// var label = PostSchedule.IsScheduled(post.Status, post.PublishedOn, timeProvider.GetUtcNow()) ? "Scheduled" : "Published";
/// </code>
/// </example>
public static class PostSchedule
{
    /// <summary>Whether the post is published with a publish time after <paramref name="now"/>.</summary>
    public static bool IsScheduled(PostStatus status, DateTimeOffset? publishedOn, DateTimeOffset now)
    {
        return status == PostStatus.Published && publishedOn > now;
    }

    /// <summary>Whether the post is published and its publish time has passed, so readers can see it (trash aside).</summary>
    public static bool IsLive(PostStatus status, DateTimeOffset? publishedOn, DateTimeOffset now)
    {
        return status == PostStatus.Published && publishedOn is { } date && date <= now;
    }
}
