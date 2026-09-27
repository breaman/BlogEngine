namespace BlogEngine.Server.Services;

/// <summary>
/// Structured log events for tag management (design 6.4, 18, O3), with stable event ids and names so they can be found
/// in Serilog or OpenTelemetry by <c>EventId.Name</c>.
/// </summary>
internal static partial class TagLog
{
    /// <summary>A tag was renamed, or its slug or description changed.</summary>
    [LoggerMessage(EventId = 5001, EventName = "TagUpdated", Level = LogLevel.Information,
        Message = "Tag {TagId} updated from {OldName} ({OldSlug}) to {Name} ({Slug}) by user {UserId}")]
    public static partial void TagUpdated(ILogger logger, int tagId, string oldName, string oldSlug, string name, string slug, int userId);

    /// <summary>A tag was merged into another one.</summary>
    [LoggerMessage(EventId = 5002, EventName = "TagMerged", Level = LogLevel.Information,
        Message = "Tag {TagId} ({Slug}) merged into tag {TargetTagId} ({TargetSlug}) by user {UserId}; {PostCount} posts retagged")]
    public static partial void TagMerged(ILogger logger, int tagId, string slug, int targetTagId, string targetSlug, int postCount, int userId);

    /// <summary>An unused tag was deleted.</summary>
    [LoggerMessage(EventId = 5003, EventName = "TagDeleted", Level = LogLevel.Information,
        Message = "Tag {TagId} ({Slug}) deleted by user {UserId}")]
    public static partial void TagDeleted(ILogger logger, int tagId, string slug, int userId);
}