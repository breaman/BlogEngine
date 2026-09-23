namespace BlogEngine.Shared.Markdown;

/// <summary>
/// Host-specific settings for <see cref="BlogMarkdownPipeline"/>.
/// </summary>
public sealed class BlogMarkdownOptions
{
    /// <summary>
    /// Host names that belong to the blog itself, such as <c>blog.example.com</c>. Absolute links to these
    /// hosts are treated as internal and don't get the external-link treatment. Relative links are always
    /// internal.
    /// </summary>
    public IReadOnlyCollection<string> InternalHosts { get; init; } = [];
}