namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Plain "share this post" links for the post page (design 14.2). They are ordinary links to each network's
/// share page, not embedded widgets, so no third-party script or tracker is loaded.
/// </summary>
public static class ShareLinks
{
    /// <summary>One share link.</summary>
    /// <param name="Network">Name shown to the reader.</param>
    /// <param name="Icon">Bootstrap Icons class.</param>
    /// <param name="Href">The share URL, with the post's title and address filled in.</param>
    public sealed record Link(string Network, string Icon, string Href);

    /// <summary>Share links for the post <paramref name="title"/> at <paramref name="absoluteUrl"/>.</summary>
    /// <example>
    /// <code>
    /// var links = ShareLinks.For("Hello", "https://blog.example/posts/2026/09/22/hello");
    /// </code>
    /// </example>
    public static IReadOnlyList<Link> For(string title, string absoluteUrl)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteUrl);

        var url = Uri.EscapeDataString(absoluteUrl);
        var text = Uri.EscapeDataString($"{title} {absoluteUrl}");

        return
        [
            // Mastodon has no single share URL (every server is different); Toot asks the reader for theirs.
            new("Mastodon", "bi-mastodon", $"https://toot.kytta.dev/?text={text}"),
            new("Bluesky", "bi-bluesky", $"https://bsky.app/intent/compose?text={text}"),
            new("LinkedIn", "bi-linkedin", $"https://www.linkedin.com/sharing/share-offsite/?url={url}"),
            new("Email", "bi-envelope", $"mailto:?subject={Uri.EscapeDataString(title)}&body={url}")
        ];
    }
}