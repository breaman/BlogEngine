namespace BlogEngine.Server.Services;

/// <summary>
/// Structured log events for posts (design 18, T4.27). Each has a stable event id and name, so the events can be found in
/// Serilog or OpenTelemetry by <c>EventId.Name</c> and filtered on their properties.
/// </summary>
internal static partial class PostLog
{
    /// <summary>A post was published, or scheduled for a future date (<paramref name="isScheduled"/>).</summary>
    [LoggerMessage(EventId = 1001, EventName = "PostPublished", Level = LogLevel.Information,
        Message = "Post {PostId} ({Slug}) published for {PublishedOn} by user {UserId}; scheduled: {IsScheduled}")]
    public static partial void PostPublished(ILogger logger, int postId, string slug, DateTimeOffset publishedOn, bool isScheduled,
        int userId);

    /// <summary>A published or scheduled post went back to draft.</summary>
    [LoggerMessage(EventId = 1002, EventName = "PostUnpublished", Level = LogLevel.Information,
        Message = "Post {PostId} ({Slug}) unpublished by user {UserId}; was scheduled: {WasScheduled}")]
    public static partial void PostUnpublished(ILogger logger, int postId, string slug, bool wasScheduled, int userId);

    /// <summary>A post was moved to the trash.</summary>
    [LoggerMessage(EventId = 1003, EventName = "PostTrashed", Level = LogLevel.Information,
        Message = "Post {PostId} ({Slug}) moved to the trash by user {UserId}; was published: {WasPublished}")]
    public static partial void PostTrashed(ILogger logger, int postId, string slug, bool wasPublished, int userId);

    /// <summary>A post was restored from the trash as a draft.</summary>
    [LoggerMessage(EventId = 1004, EventName = "PostRestored", Level = LogLevel.Information,
        Message = "Post {PostId} ({Slug}) restored from the trash by user {UserId}")]
    public static partial void PostRestored(ILogger logger, int postId, string slug, int userId);

    /// <summary>Posts in the trash were deleted permanently, one by one or by emptying the trash.</summary>
    [LoggerMessage(EventId = 1005, EventName = "PostsPurged", Level = LogLevel.Information,
        Message = "{Count} posts deleted permanently from the trash by user {UserId}: {PostIds}")]
    public static partial void PostsPurged(ILogger logger, int count, string postIds, int userId);
}