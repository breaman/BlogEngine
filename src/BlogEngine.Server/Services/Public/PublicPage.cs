using System.ComponentModel;

using BlogEngine.Shared.Common;

namespace BlogEngine.Server.Services.Public;

// Immutable, sealed and marked [ImmutableObject(true)], so HybridCache hands the cached instance to every
// request instead of deserializing a copy each time. Nothing may mutate it after construction.

/// <summary>A published standalone page, as the navigation and the sitemap need it (design 6.7, A17).</summary>
/// <param name="Id">Page id.</param>
/// <param name="Title">Title, also the navigation label.</param>
/// <param name="Slug">URL slug.</param>
/// <param name="ShowInNav">Whether the page is linked from the site navigation.</param>
/// <param name="NavOrder">Position in the navigation; lower values come first.</param>
/// <param name="ModifiedOn">When the page was last saved, for the sitemap's <c>lastmod</c>.</param>
[ImmutableObject(true)]
public sealed record PublicPageSummary(int Id, string Title, string Slug, bool ShowInNav, int NavOrder, DateTimeOffset? ModifiedOn)
{
    /// <summary>The page's URL path, <c>/{slug}</c>.</summary>
    public string Path => SitePaths.Page(Slug);
}

/// <summary>
/// Every published page (summaries only), in navigation order: the snapshot behind the navigation links, the sitemap
/// and slug lookups (see <see cref="PublicPageQueries"/>).
/// </summary>
[ImmutableObject(true)]
public sealed class PublicPageIndex
{
    private readonly Dictionary<string, PublicPageSummary> pagesBySlug;

    /// <summary>Builds the index and its slug lookup.</summary>
    /// <param name="pages">The published pages, in navigation order.</param>
    public PublicPageIndex(IReadOnlyList<PublicPageSummary> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        Pages = pages;
        NavPages = [.. pages.Where(p => p.ShowInNav)];

        // Slugs are stored lowercase and unique; lookups lowercase the request (as in PublicPostIndex).
        pagesBySlug = pages.ToDictionary(p => p.Slug, StringComparer.Ordinal);
    }

    /// <summary>The published pages, in navigation order and then by title.</summary>
    public IReadOnlyList<PublicPageSummary> Pages { get; }

    /// <summary>The published pages linked from the navigation, in order.</summary>
    public IReadOnlyList<PublicPageSummary> NavPages { get; }

    /// <summary>The published page with this slug (case-insensitive), if any.</summary>
    public PublicPageSummary? Find(string? slug)
    {
        return slug is not null && pagesBySlug.TryGetValue(slug.ToLowerInvariant(), out var page) ? page : null;
    }

    /// <summary>A snapshot with no pages.</summary>
    public static PublicPageIndex Empty { get; } = new([]);
}

/// <summary>A published page with its rendered content.</summary>
/// <param name="Page">The summary fields.</param>
/// <param name="Html">The sanitized HTML rendered at save time, with in-page links resolved (<see cref="FragmentLinks"/>).</param>
/// <param name="HasCodeBlocks">Whether the page needs the code highlighting script (design 10.3).</param>
/// <param name="Summary">The summary, the default meta description.</param>
/// <param name="MetaTitle">Optional SEO title override.</param>
/// <param name="MetaDescription">Optional SEO description override.</param>
/// <param name="Outline">The table of contents; empty for short pages (<see cref="PostOutline"/>).</param>
[ImmutableObject(true)]
public sealed record PublicPageContent(
    PublicPageSummary Page,
    string Html,
    bool HasCodeBlocks,
    string Summary,
    string? MetaTitle,
    string? MetaDescription,
    IReadOnlyList<OutlineHeading> Outline);