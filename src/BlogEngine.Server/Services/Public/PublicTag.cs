using System.ComponentModel;

using BlogEngine.Shared.Common;

namespace BlogEngine.Server.Services.Public;

// Immutable, sealed and marked [ImmutableObject(true)], so HybridCache hands the cached instance to every
// request instead of deserializing a copy each time. Nothing may mutate it after construction.

/// <summary>A tag with at least one visible post, for the tag index and tag pages.</summary>
/// <param name="Id">Tag id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL slug.</param>
/// <param name="Description">Optional description shown on the tag page.</param>
/// <param name="PostCount">How many visible posts have the tag.</param>
[ImmutableObject(true)]
public sealed record PublicTag(int Id, string Name, string Slug, string? Description, int PostCount)
{
    /// <summary>The tag page, <c>/tags/{slug}</c>.</summary>
    public string Path => TagPaths.Tag(Slug);
}
