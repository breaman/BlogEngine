namespace BlogEngine.Shared.Contracts;

/// <summary>
/// One page of a larger result set.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="TotalCount">How many items match across all pages.</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">The requested page size.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>Number of pages; at least 1 so an empty result still has a page to show.</summary>
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
}