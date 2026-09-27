using BlogEngine.Shared.Contracts;

namespace BlogEngine.Client.Services;

/// <summary>
/// A copy of the editor's fields kept in the browser's <c>localStorage</c> (design 10.2, T1.13), so the author
/// can get unsaved work back after a crash, a closed tab or a lost connection.
/// </summary>
/// <remarks>
/// Only the fields the author edits are kept. Server-computed values (status, dates, <c>RowVersion</c>) always
/// come from the server, so restoring a backup never resurrects a stale concurrency token.
/// </remarks>
public sealed class PostDraftBackup
{
    /// <summary>When the backup was written (UTC, browser clock).</summary>
    public DateTimeOffset SavedAt { get; set; }

    /// <summary>Post title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>URL slug as typed.</summary>
    public string? Slug { get; set; }

    /// <summary>Summary as typed.</summary>
    public string? Summary { get; set; }

    /// <summary>Markdown content.</summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    /// <summary>Tag names.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Whether readers may comment.</summary>
    public bool AllowComments { get; set; }

    /// <summary>Whether the post is featured.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>SEO title override.</summary>
    public string? MetaTitle { get; set; }

    /// <summary>SEO description override.</summary>
    public string? MetaDescription { get; set; }

    /// <summary>Cover image id.</summary>
    public int? CoverMediaId { get; set; }

    /// <summary>Cover image thumbnail details, so a restored cover shows without another request.</summary>
    public PostImageDto? CoverImage { get; set; }

    /// <summary>Social image id.</summary>
    public int? SocialImageMediaId { get; set; }

    /// <summary>Social image thumbnail details.</summary>
    public PostImageDto? SocialImage { get; set; }

    /// <summary>Captures the editable fields of <paramref name="post"/>.</summary>
    public static PostDraftBackup From(PostEditDto post, DateTimeOffset savedAt)
    {
        ArgumentNullException.ThrowIfNull(post);

        return new PostDraftBackup
        {
            SavedAt = savedAt,
            Title = post.Title,
            Slug = post.Slug,
            Summary = post.Summary,
            ContentMarkdown = post.ContentMarkdown,
            Tags = [.. post.Tags],
            AllowComments = post.AllowComments,
            IsFeatured = post.IsFeatured,
            MetaTitle = post.MetaTitle,
            MetaDescription = post.MetaDescription,
            CoverMediaId = post.CoverMediaId,
            CoverImage = post.CoverImage,
            SocialImageMediaId = post.SocialImageMediaId,
            SocialImage = post.SocialImage
        };
    }

    /// <summary>Copies the backed-up fields onto <paramref name="post"/>, leaving server-computed values alone.</summary>
    public void ApplyTo(PostEditDto post)
    {
        ArgumentNullException.ThrowIfNull(post);

        post.Title = Title;
        post.Slug = Slug;
        post.Summary = Summary;
        post.ContentMarkdown = ContentMarkdown;
        post.Tags = [.. Tags];
        post.AllowComments = AllowComments;
        post.IsFeatured = IsFeatured;
        post.MetaTitle = MetaTitle;
        post.MetaDescription = MetaDescription;
        post.CoverMediaId = CoverMediaId;
        post.CoverImage = CoverImage;
        post.SocialImageMediaId = SocialImageMediaId;
        post.SocialImage = SocialImage;
    }

    /// <summary>Whether restoring this backup would change anything in <paramref name="post"/>.</summary>
    public bool DiffersFrom(PostEditDto post)
    {
        ArgumentNullException.ThrowIfNull(post);

        var restored = post.Clone();
        ApplyTo(restored);
        return !PostEdits.AreEqual(restored, post);
    }
}