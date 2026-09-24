using System.ComponentModel;

namespace BlogEngine.Server.Services.Public;

// Immutable, sealed and marked [ImmutableObject(true)], so HybridCache hands the cached instance to every
// request instead of deserializing a copy each time. Nothing may mutate it after construction.

/// <summary>A visible post with its rendered content, for the post page and feeds.</summary>
/// <param name="Post">The list-page fields.</param>
/// <param name="Html">The sanitized HTML rendered at save time.</param>
/// <param name="HasCodeBlocks">Whether the page needs the code highlighting script (design 10.3).</param>
/// <param name="MetaTitle">Optional SEO title override.</param>
/// <param name="MetaDescription">Optional SEO description override.</param>
/// <param name="AllowComments">Whether the post takes comments at all (design 8.5).</param>
/// <param name="CommentsCloseOn">When comments close, if they do.</param>
/// <param name="Cover">The cover image shown above the content (A15), if any.</param>
/// <param name="SocialImageOverride">The image chosen for social sharing previews (A16), if any.</param>
/// <param name="Outline">
/// The table of contents (P13); empty for short posts, and for feed items, which never show one. See <see cref="PostOutline"/>.
/// </param>
[ImmutableObject(true)]
public sealed record PublicPostContent(
    PublicPostSummary Post,
    string Html,
    bool HasCodeBlocks,
    string? MetaTitle,
    string? MetaDescription,
    bool AllowComments = true,
    DateTimeOffset? CommentsCloseOn = null,
    PublicImage? Cover = null,
    PublicImage? SocialImageOverride = null,
    IReadOnlyList<OutlineHeading>? Outline = null)
{
    /// <summary>The table of contents entries; empty when the post doesn't get one.</summary>
    public IReadOnlyList<OutlineHeading> TableOfContents => Outline ?? [];

    /// <summary>The image for social sharing previews: the chosen social image, otherwise the cover (A15, A16).</summary>
    public PublicImage? SocialImage => SocialImageOverride ?? Cover;

    /// <summary>
    /// Whether readers may comment at <paramref name="now"/> (design 8.5): the post allows comments and its closing time,
    /// if any, hasn't passed. The site-wide switch is checked separately.
    /// </summary>
    public bool CommentsOpenAt(DateTimeOffset now)
    {
        return AllowComments && (CommentsCloseOn is not { } closeOn || closeOn > now);
    }
}
