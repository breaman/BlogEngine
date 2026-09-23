using System.ComponentModel;

namespace BlogEngine.Server.Services.Public;

// Immutable, sealed and marked [ImmutableObject(true)], so HybridCache hands the cached instance to every
// request instead of deserializing a copy each time. Nothing may mutate it after construction.

/// <summary>One page of a post list.</summary>
/// <param name="Posts">The posts on this page, newest first.</param>
/// <param name="TotalCount">How many posts the whole list has.</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">Posts per page.</param>
[ImmutableObject(true)]
public sealed record PublicPostPage(IReadOnlyList<PublicPostSummary> Posts, int TotalCount, int Page, int PageSize)
{
    /// <summary>Number of pages; at least 1, so an empty list still has a first page.</summary>
    public int TotalPages => Math.Max(1, (TotalCount + PageSize - 1) / Math.Max(1, PageSize));

    /// <summary>Whether <see cref="Page"/> is a page the list has; out-of-range pages answer 404.</summary>
    public bool IsPageInRange => Page >= 1 && Page <= TotalPages;

    /// <summary>Slices <paramref name="posts"/> (already in display order) into page <paramref name="page"/>.</summary>
    public static PublicPostPage Create(IReadOnlyList<PublicPostSummary> posts, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(posts);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        // Skip is computed in long arithmetic so an absurd ?page= can't overflow.
        var skip = Math.Clamp(((long)page - 1) * pageSize, 0, posts.Count);
        var items = posts.Skip((int)skip).Take(page >= 1 ? pageSize : 0).ToArray();

        return new PublicPostPage(items, posts.Count, page, pageSize);
    }
}
