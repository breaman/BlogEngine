namespace BlogEngine.Shared.Enums;

/// <summary>
/// What the home page shows (design 13).
/// </summary>
/// <remarks>Values are persisted as integers and must never be renumbered.</remarks>
public enum HomePageMode
{
    /// <summary>Featured posts pinned first, then the latest posts.</summary>
    FeaturedThenLatest = 0,

    /// <summary>Only the latest posts; the featured flag is ignored.</summary>
    LatestPosts = 1
}