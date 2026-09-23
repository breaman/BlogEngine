using System.ComponentModel.DataAnnotations;

using BlogEngine.Shared.Common;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Validation;

namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A post as edited in the admin editor (design 6.2, 10.2): the fields the author edits, plus read-only
/// values the server computes on save.
/// </summary>
/// <remarks>
/// <para>
/// A mutable class rather than a record so the editor form can bind to it directly. Validate it with
/// <see cref="PostEditValidator"/>, which adds the rules data annotations can't express (tag names).
/// </para>
/// <para>
/// Blank <see cref="Slug"/> and <see cref="Summary"/> mean "generate it": the slug from the title (only
/// until the first publish) and the summary from the content.
/// </para>
/// </remarks>
public sealed class PostEditDto
{
    /// <summary>The post id; 0 for a post that hasn't been created yet.</summary>
    public int Id { get; set; }

    /// <summary>Post title.</summary>
    [Required(ErrorMessage = "A title is required.")]
    [MaxLength(FieldLengths.PostTitle, ErrorMessage = "The title can be at most {1} characters.")]
    public string Title { get; set; } = string.Empty;

    /// <summary>URL slug; blank to generate it from the title.</summary>
    [MaxLength(FieldLengths.Slug, ErrorMessage = "The slug can be at most {1} characters.")]
    [RegularExpression(ValidationPatterns.Slug,
        ErrorMessage = "The slug may contain only lowercase letters, digits and single hyphens between words.")]
    public string? Slug { get; set; }

    /// <summary>Listing summary and default meta description; blank to generate it from the content.</summary>
    [MaxLength(FieldLengths.PostSummary, ErrorMessage = "The summary can be at most {1} characters.")]
    public string? Summary { get; set; }

    /// <summary>Markdown source.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>Tag names as typed; matched to existing tags case-insensitively on save.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Whether readers may comment.</summary>
    public bool AllowComments { get; set; } = true;

    /// <summary>Pins the post to the top of the home page.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>Optional SEO title override.</summary>
    [MaxLength(FieldLengths.MetaTitle, ErrorMessage = "The meta title can be at most {1} characters.")]
    public string? MetaTitle { get; set; }

    /// <summary>Optional SEO description override.</summary>
    [MaxLength(FieldLengths.MetaDescription, ErrorMessage = "The meta description can be at most {1} characters.")]
    public string? MetaDescription { get; set; }

    /// <summary>
    /// Concurrency token from the last load or save. Required for updates; a stale value makes the save
    /// fail with a conflict instead of overwriting changes made in another tab.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    // Computed by the server; ignored when sent back.

    /// <summary>Draft or published (read-only; change it through publish and unpublish).</summary>
    public PostStatus Status { get; set; }

    /// <summary>When the post was published (UTC), or <see langword="null"/> if it never was.</summary>
    public DateTimeOffset? PublishedOn { get; set; }

    /// <summary>The publish date in the blog's time zone, used in the URL.</summary>
    public DateOnly? PublishedDateLocal { get; set; }

    /// <summary>When the content last changed after publishing.</summary>
    public DateTimeOffset? LastUpdatedOn { get; set; }

    /// <summary>When the post was last saved.</summary>
    public DateTimeOffset? ModifiedOn { get; set; }

    /// <summary>Readable words in the content.</summary>
    public int WordCount { get; set; }

    /// <summary>Estimated reading time in minutes.</summary>
    public int ReadingMinutes { get; set; }

    /// <summary>The public URL path while the post is published, otherwise <see langword="null"/>.</summary>
    public string? PublicPath { get; set; }

    /// <summary>Autosaved changes to a published post that aren't live yet, if any.</summary>
    public PostPendingChangesDto? PendingChanges { get; set; }
}
