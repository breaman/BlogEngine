namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Filters and paging for the media library (<c>GET /api/admin/media</c>, design 7.4).
/// </summary>
public sealed class MediaListQuery
{
    /// <summary>Default page size; a multiple of the grid's 2, 3, 4 and 6 columns.</summary>
    public const int DefaultPageSize = 24;

    /// <summary>Largest page size a caller may ask for.</summary>
    public const int MaxPageSize = 96;

    /// <summary>Text to find in the file name, alt text or caption.</summary>
    public string? Search { get; set; }

    /// <summary>Only items no post uses (design 9.6).</summary>
    public bool Unused { get; set; }

    /// <summary>1-based page number; values below 1 are treated as 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Items per page, clamped to 1 through <see cref="MaxPageSize"/>.</summary>
    public int PageSize { get; set; } = DefaultPageSize;
}