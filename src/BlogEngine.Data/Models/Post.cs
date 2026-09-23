using BlogEngine.Data.Interfaces;
using BlogEngine.Shared.Enums;

namespace BlogEngine.Data.Models;

/// <summary>
/// A blog post (design 6.2). Markdown is the source of truth; the rendered HTML is cached alongside it.
/// </summary>
/// <remarks>
/// A post is publicly visible only when it is <see cref="PostStatus.Published"/>, its
/// <see cref="PublishedOn"/> has passed, and it is not in the trash (design 6.3).
/// </remarks>
public class Post : FingerPrintEntityBase, ISoftDeletable
{
    /// <summary>Post title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Lowercase <c>[a-z0-9-]</c> URL slug, unique across all posts.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Summary for list pages and the default meta description; generated from the content when left blank.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Markdown source.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>Sanitized HTML rendered from <see cref="ContentMarkdown"/>, regenerated on save or when referenced media changes.</summary>
    public string ContentHtml { get; set; } = string.Empty;

    /// <summary>Draft or published.</summary>
    public PostStatus Status { get; set; }

    /// <summary>When the post was (or will be) published, in UTC. A future value means scheduled.</summary>
    public DateTimeOffset? PublishedOn { get; set; }

    /// <summary>
    /// The publish date in the blog's configured time zone. Drives the URL date segments and archive
    /// queries, and is recomputed on save and when the time zone setting changes.
    /// </summary>
    public DateOnly? PublishedDateLocal { get; set; }

    /// <summary>When the content last changed after publishing; shown as "Updated …".</summary>
    public DateTimeOffset? LastUpdatedOn { get; set; }

    /// <summary>Optional cover image from the media library.</summary>
    public int? CoverMediaId { get; set; }

    /// <summary>The cover image.</summary>
    public MediaItem? CoverMedia { get; set; }

    /// <summary>Whether readers may comment on this post.</summary>
    public bool AllowComments { get; set; } = true;

    /// <summary>When comments close, derived from the auto-close setting at publish time.</summary>
    public DateTimeOffset? CommentsCloseOn { get; set; }

    /// <summary>Pins the post to the top of the home page.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>Word count of the content, excluding code; computed on save.</summary>
    public int WordCount { get; set; }

    /// <summary>Estimated reading time in minutes; computed on save.</summary>
    public int ReadingMinutes { get; set; }

    /// <summary>Optional SEO title override.</summary>
    public string? MetaTitle { get; set; }

    /// <summary>Optional SEO description override.</summary>
    public string? MetaDescription { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedOn { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c> used as the optimistic concurrency token, so two browser tabs cannot
    /// silently overwrite each other's edits.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Tags on this post (skip navigation through <see cref="PostTags"/>).</summary>
    public List<Tag> Tags { get; set; } = [];

    /// <summary>Join rows linking this post to its tags.</summary>
    public List<PostTag> PostTags { get; set; } = [];

    /// <summary>Reader comments and author replies.</summary>
    public List<Comment> Comments { get; set; } = [];

    /// <summary>Saved revisions of the title and content.</summary>
    public List<PostRevision> Revisions { get; set; } = [];

    /// <summary>Media library items referenced by the content.</summary>
    public List<PostMedia> PostMedia { get; set; } = [];

    /// <summary>Private preview links for this post.</summary>
    public List<PreviewToken> PreviewTokens { get; set; } = [];
}
