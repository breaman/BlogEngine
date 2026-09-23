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
[ImmutableObject(true)]
public sealed record PublicPostContent(
    PublicPostSummary Post,
    string Html,
    bool HasCodeBlocks,
    string? MetaTitle,
    string? MetaDescription);
