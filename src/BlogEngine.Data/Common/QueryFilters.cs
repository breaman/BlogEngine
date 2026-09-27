namespace BlogEngine.Data.Common;

/// <summary>
/// Names of the model's global query filters (EF Core 10 named filters).
/// </summary>
/// <remarks>
/// Naming the filters lets a query switch off just one of them, for example
/// <c>db.Posts.IgnoreQueryFilters([QueryFilters.SoftDelete])</c> for the admin trash view, without
/// silently dropping any filter added later.
/// </remarks>
public static class QueryFilters
{
    /// <summary>Hides trashed posts and pages, and rows that belong to a trashed post.</summary>
    public const string SoftDelete = "SoftDelete";
}