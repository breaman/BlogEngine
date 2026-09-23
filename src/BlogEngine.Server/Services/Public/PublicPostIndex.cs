using System.ComponentModel;

namespace BlogEngine.Server.Services.Public;

// Immutable, sealed and marked [ImmutableObject(true)], so HybridCache hands the cached instance to every
// request instead of deserializing a copy each time. Nothing may mutate it after construction.

/// <summary>
/// Every visible post (summaries only) and every tag with visible posts: the snapshot that all public lists,
/// archives, tag pages and the sitemap are computed from (see <see cref="PublicPostQueries"/>).
/// </summary>
[ImmutableObject(true)]
public sealed class PublicPostIndex
{
    private readonly Dictionary<string, PublicPostSummary> postsBySlug;
    private readonly Dictionary<string, PublicTag> tagsBySlug;

    /// <summary>Builds the index and its slug lookups.</summary>
    /// <param name="posts">The visible posts, newest first.</param>
    /// <param name="tags">The tags with visible posts, by name.</param>
    public PublicPostIndex(IReadOnlyList<PublicPostSummary> posts, IReadOnlyList<PublicTag> tags)
    {
        ArgumentNullException.ThrowIfNull(posts);
        ArgumentNullException.ThrowIfNull(tags);

        Posts = posts;
        Tags = tags;

        // Slugs are stored lowercase and unique; lookups lowercase the request instead of relying on a
        // comparer, which would not survive serialization if a distributed cache is ever added.
        postsBySlug = posts.ToDictionary(p => p.Slug, StringComparer.Ordinal);
        tagsBySlug = tags.ToDictionary(t => t.Slug, StringComparer.Ordinal);
    }

    /// <summary>The visible posts, newest first.</summary>
    public IReadOnlyList<PublicPostSummary> Posts { get; }

    /// <summary>The tags that have at least one visible post, ordered by name.</summary>
    public IReadOnlyList<PublicTag> Tags { get; }

    /// <summary>The visible post with this slug (case-insensitive), if any.</summary>
    public PublicPostSummary? FindPost(string? slug)
    {
        return slug is not null && postsBySlug.TryGetValue(slug.ToLowerInvariant(), out var post) ? post : null;
    }

    /// <summary>The tag with this slug (case-insensitive) if it has visible posts.</summary>
    public PublicTag? FindTag(string? slug)
    {
        return slug is not null && tagsBySlug.TryGetValue(slug.ToLowerInvariant(), out var tag) ? tag : null;
    }

    /// <summary>A snapshot with no posts, for a blog that hasn't published anything yet.</summary>
    public static PublicPostIndex Empty { get; } = new([], []);
}
