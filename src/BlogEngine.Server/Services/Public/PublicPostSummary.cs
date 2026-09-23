using System.ComponentModel;

using BlogEngine.Shared.Common;

namespace BlogEngine.Server.Services.Public;

// Immutable, sealed and marked [ImmutableObject(true)], so HybridCache hands the cached instance to every
// request instead of deserializing a copy each time. Nothing may mutate it after construction.

/// <summary>What list pages show about a visible post (design 14.1).</summary>
/// <param name="Id">Post id.</param>
/// <param name="Title">Title.</param>
/// <param name="Slug">URL slug.</param>
/// <param name="Summary">Summary (the author's, or generated from the content).</param>
/// <param name="PublishedOn">When the post was published, in UTC.</param>
/// <param name="PublishedDateLocal">The publish date in the blog's time zone, as used in the URL.</param>
/// <param name="LastUpdatedOn">When the content last changed after publishing, if it did.</param>
/// <param name="ReadingMinutes">Estimated reading time.</param>
/// <param name="IsFeatured">Whether the post is pinned to the home page.</param>
/// <param name="Tags">The post's tags, by name.</param>
[ImmutableObject(true)]
public sealed record PublicPostSummary(
    int Id,
    string Title,
    string Slug,
    string Summary,
    DateTimeOffset PublishedOn,
    DateOnly PublishedDateLocal,
    DateTimeOffset? LastUpdatedOn,
    int ReadingMinutes,
    bool IsFeatured,
    IReadOnlyList<PublicTagLink> Tags)
{
    /// <summary>The canonical post URL path, <c>/posts/{yyyy}/{mm}/{dd}/{slug}</c>.</summary>
    public string Path => PostPaths.Post(PublishedDateLocal, Slug);

    /// <summary>When the post last changed: the update date if it has one, otherwise the publish date.</summary>
    public DateTimeOffset LastModified => LastUpdatedOn ?? PublishedOn;
}
