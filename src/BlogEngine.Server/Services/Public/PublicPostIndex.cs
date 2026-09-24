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
    private readonly Dictionary<int, int> positionsById;

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
        positionsById = posts.Select((post, position) => (post.Id, position)).ToDictionary(p => p.Id, p => p.position);
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

    /// <summary>
    /// The visible posts published just after and just before the post (design 14.2, P10), in the same order as the
    /// lists; either is <see langword="null"/> at the ends, and both are when the post isn't in the snapshot.
    /// </summary>
    public PublicPostNeighbors GetNeighbors(int postId)
    {
        if (!positionsById.TryGetValue(postId, out var position))
        {
            return PublicPostNeighbors.None;
        }

        // Posts are newest first, so the newer post comes before this one in the list.
        return new PublicPostNeighbors(
            Newer: position > 0 ? Posts[position - 1] : null,
            Older: position < Posts.Count - 1 ? Posts[position + 1] : null);
    }

    /// <summary>
    /// Up to <paramref name="count"/> other visible posts sharing tags with <paramref name="post"/> (design 14.2, P10):
    /// the most shared tags first, then the newest.
    /// </summary>
    public IReadOnlyList<PublicPostSummary> GetRelated(PublicPostSummary post, int count)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (count <= 0 || post.Tags.Count == 0)
        {
            return [];
        }

        var tagIds = post.Tags.Select(t => t.Id).ToHashSet();

        // The list position (newest first) breaks ties, so equally related posts come newest first.
        return
        [
            .. Posts
                .Select((candidate, position) => (Post: candidate, Position: position, Shared: candidate.Tags.Count(t => tagIds.Contains(t.Id))))
                .Where(c => c.Shared > 0 && c.Post.Id != post.Id)
                .OrderByDescending(c => c.Shared)
                .ThenBy(c => c.Position)
                .Take(count)
                .Select(c => c.Post)
        ];
    }

    /// <summary>A snapshot with no posts, for a blog that hasn't published anything yet.</summary>
    public static PublicPostIndex Empty { get; } = new([], []);
}
