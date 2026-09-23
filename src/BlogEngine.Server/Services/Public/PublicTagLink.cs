using System.ComponentModel;

using BlogEngine.Shared.Common;

namespace BlogEngine.Server.Services.Public;

// Immutable, sealed and marked [ImmutableObject(true)], so HybridCache hands the cached instance to every
// request instead of deserializing a copy each time. Nothing may mutate it after construction.

/// <summary>A tag as linked from a post.</summary>
/// <param name="Id">Tag id.</param>
/// <param name="Name">Display name, such as <c>C#</c>.</param>
/// <param name="Slug">URL slug, such as <c>csharp</c>.</param>
[ImmutableObject(true)]
public sealed record PublicTagLink(int Id, string Name, string Slug)
{
    /// <summary>The tag page, <c>/tags/{slug}</c>.</summary>
    public string Path => TagPaths.Tag(Slug);
}
