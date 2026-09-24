using BlogEngine.Data.Interfaces;
using BlogEngine.Shared.Enums;

namespace BlogEngine.Data.Models;

/// <summary>
/// A standalone page such as <c>/about</c> (design 6.7). Uses the same Markdown fields as a post but has
/// no date or tags, and can optionally appear in the site navigation.
/// </summary>
public class Page : FingerPrintEntityBase, ISoftDeletable
{
    /// <summary>Page title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Lowercase URL slug served at <c>/{slug}</c>; unique, and never a reserved route word.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Summary used as the default meta description.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Markdown source.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>Sanitized HTML rendered from <see cref="ContentMarkdown"/>.</summary>
    public string ContentHtml { get; set; } = string.Empty;

    /// <summary>
    /// Whether <see cref="ContentHtml"/> has code blocks, so the public page loads the highlighting script only when it
    /// is needed (design 10.3), as for posts.
    /// </summary>
    public bool HasCodeBlocks { get; set; }

    /// <summary>Draft or published; pages have no scheduling.</summary>
    public PostStatus Status { get; set; }

    /// <summary>Whether the page is linked from the site navigation.</summary>
    public bool ShowInNav { get; set; }

    /// <summary>Position in the navigation; lower values come first.</summary>
    public int NavOrder { get; set; }

    /// <summary>Optional SEO title override.</summary>
    public string? MetaTitle { get; set; }

    /// <summary>Optional SEO description override.</summary>
    public string? MetaDescription { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedOn { get; set; }
}
