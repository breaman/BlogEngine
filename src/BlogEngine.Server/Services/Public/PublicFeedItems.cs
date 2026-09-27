using System.ComponentModel;

namespace BlogEngine.Server.Services.Public;

// Immutable, sealed and marked [ImmutableObject(true)], so HybridCache hands the cached instance to every
// request instead of deserializing a copy each time. Nothing may mutate it after construction.

/// <summary>
/// The posts of one feed, newest first. A dedicated type rather than a bare list, because HybridCache only
/// reuses cached instances of sealed immutable types; a list would be serialized and copied on every read.
/// </summary>
/// <param name="Posts">The feed's posts with their content.</param>
[ImmutableObject(true)]
public sealed record PublicFeedItems(IReadOnlyList<PublicPostContent> Posts);