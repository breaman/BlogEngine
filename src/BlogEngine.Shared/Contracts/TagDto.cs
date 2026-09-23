namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A tag as offered by tag autocomplete and tag management (design 6.4).
/// </summary>
public sealed class TagDto
{
    /// <summary>The tag id.</summary>
    public int Id { get; set; }

    /// <summary>Display name with its original casing, for example <c>C#</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL slug, for example <c>csharp</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Number of posts (not in the trash) with this tag.</summary>
    public int PostCount { get; set; }
}
