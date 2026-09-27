namespace BlogEngine.Shared.Contracts;

/// <summary>
/// Result of a slug check.
/// </summary>
/// <param name="Slug">The slug that was checked (generated from the title when none was given).</param>
/// <param name="IsValid">Whether the slug has the allowed format.</param>
/// <param name="IsAvailable">Whether no other post (including trashed ones) uses the slug.</param>
/// <param name="Suggestion">The slug a save would actually use: <paramref name="Slug"/> or a free <c>-2</c>, <c>-3</c>, … variant.</param>
/// <param name="Message">A message to show next to the slug field, or <see langword="null"/> when all is well.</param>
public sealed record SlugCheckResult(string Slug, bool IsValid, bool IsAvailable, string? Suggestion, string? Message);