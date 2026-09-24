using System.ComponentModel;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// A media library image shown on the public site outside a post's content: its cover (A15) or social image (A16).
/// </summary>
/// <remarks>Immutable, like the other cached public records, so <c>HybridCache</c> can share one instance.</remarks>
/// <param name="Url">Site-relative URL of the current version, with the cache-busting <c>?v=</c>.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="AltText">The library's alt text; empty for a decorative image.</param>
[ImmutableObject(true)]
public sealed record PublicImage(string Url, int Width, int Height, string AltText);
