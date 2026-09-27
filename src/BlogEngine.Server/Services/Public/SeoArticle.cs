namespace BlogEngine.Server.Services.Public;

/// <summary>
/// What <c>SeoHead</c> needs to describe a page as a blog post (design 14.2, P8): the Open Graph <c>article:*</c> tags
/// and the JSON-LD <c>BlogPosting</c>. Pages that aren't posts pass none and are described as a website.
/// </summary>
/// <param name="Headline">The post's own title (not the SEO title override), as JSON-LD <c>headline</c>.</param>
/// <param name="PublishedOn">When the post was published.</param>
/// <param name="ModifiedOn">When it last changed; the publish time when it never did.</param>
/// <param name="Tags">Tag names, as <c>article:tag</c> and JSON-LD <c>keywords</c>.</param>
public sealed record SeoArticle(string Headline, DateTimeOffset PublishedOn, DateTimeOffset ModifiedOn, IReadOnlyList<string> Tags)
{
    /// <summary>The article details of a visible post.</summary>
    public static SeoArticle From(PublicPostSummary post)
    {
        ArgumentNullException.ThrowIfNull(post);

        return new SeoArticle(post.Title, post.PublishedOn, post.LastModified, [.. post.Tags.Select(t => t.Name)]);
    }
}