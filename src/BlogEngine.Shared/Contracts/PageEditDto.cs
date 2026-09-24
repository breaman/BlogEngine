using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Validation;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A standalone page (<c>/about</c>, <c>/privacy</c>) as edited in the admin page editor (design 6.7, A17): the fields
/// the author edits, plus read-only values the server fills in.
/// </summary>
/// <remarks>
/// A mutable class so the editor form can bind to it directly; the rules are in <see cref="PageEditValidator"/>. A blank
/// <see cref="Slug"/> means "generate it from the title", and a blank <see cref="Summary"/> "generate it from the
/// content".
/// </remarks>
public sealed class PageEditDto
{
    /// <summary>The page id; 0 for a page that hasn't been created yet.</summary>
    public int Id { get; set; }

    /// <summary>Page title, also its navigation label.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>URL slug (<c>/{slug}</c>); blank to generate it from the title.</summary>
    public string? Slug { get; set; }

    /// <summary>Default meta description; blank to generate it from the content.</summary>
    public string? Summary { get; set; }

    /// <summary>Markdown source.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>Whether the page is linked from the site navigation once it is published.</summary>
    public bool ShowInNav { get; set; }

    /// <summary>Position in the navigation; lower values come first.</summary>
    public int NavOrder { get; set; }

    /// <summary>Optional SEO title override.</summary>
    public string? MetaTitle { get; set; }

    /// <summary>Optional SEO description override.</summary>
    public string? MetaDescription { get; set; }

    // Computed by the server; ignored when sent back.

    /// <summary>Draft or published (read-only; change it through publish and unpublish).</summary>
    public PostStatus Status { get; set; }

    /// <summary>When the page was last saved.</summary>
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>The public URL path while the page is published, otherwise <see langword="null"/>.</summary>
    public string? PublicPath { get; set; }

    /// <summary>A copy that shares nothing mutable with this instance, so a save can send a snapshot.</summary>
    public PageEditDto Clone()
    {
        return (PageEditDto)MemberwiseClone();
    }
}
