namespace BlogEngine.Shared.Common;

/// <summary>
/// Builds the public URL paths of the tag pages and feeds (design 7.1).
/// </summary>
/// <example>
/// <code>
/// TagPaths.Tag("csharp");  // "/tags/csharp"
/// TagPaths.Feed("csharp"); // "/tags/csharp/feed.xml"
/// </code>
/// </example>
public static class TagPaths
{
    /// <summary>The tag index with post counts.</summary>
    public const string Index = "/tags";

    /// <summary>The paginated list of posts with the tag.</summary>
    public static string Tag(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        return $"{Index}/{slug}";
    }

    /// <summary>The tag's RSS feed.</summary>
    public static string Feed(string slug)
    {
        return $"{Tag(slug)}/feed.xml";
    }
}
