namespace BlogEngine.Server.Services.Public;

/// <summary>The posts around a post in publish order, for its previous/next links (design 14.2, P10).</summary>
/// <param name="Newer">The post published next, or <see langword="null"/> for the newest post.</param>
/// <param name="Older">The post published before, or <see langword="null"/> for the oldest post.</param>
public sealed record PublicPostNeighbors(PublicPostSummary? Newer, PublicPostSummary? Older)
{
    /// <summary>No neighbors: the only post, or one that isn't visible.</summary>
    public static PublicPostNeighbors None { get; } = new(null, null);
}
