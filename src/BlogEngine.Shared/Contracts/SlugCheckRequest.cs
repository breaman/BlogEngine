namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Body of <c>POST /api/admin/posts/slug-check</c>, used for inline slug validation in the editor.
/// </summary>
public sealed class SlugCheckRequest
{
    /// <summary>The slug to check; when blank, the slug generated from <see cref="Title"/> is checked.</summary>
    public string? Slug { get; set; }

    /// <summary>The post title, used when <see cref="Slug"/> is blank.</summary>
    public string? Title { get; set; }

    /// <summary>The post being edited, so its own slug doesn't count as taken; <see langword="null"/> for a new post.</summary>
    public int? PostId { get; set; }
}