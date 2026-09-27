namespace BlogEngine.Shared.Contracts;

/// <summary>
/// One row of tag management at <c>/admin/tags</c> (design 6.4, O3): the tag with its description and usage counts.
/// </summary>
public sealed class TagAdminDto
{
    /// <summary>The tag id.</summary>
    public int Id { get; set; }

    /// <summary>Display name with its original casing, for example <c>C#</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL slug, for example <c>csharp</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Optional description shown on the public tag page.</summary>
    public string? Description { get; set; }

    /// <summary>Posts outside the trash with this tag (drafts included).</summary>
    public int PostCount { get; set; }

    /// <summary>Posts in the trash with this tag; they keep it, so the tag can't be deleted while they exist.</summary>
    public int TrashedPostCount { get; set; }

    /// <summary>Whether no post, not even one in the trash, uses the tag, so it can be deleted.</summary>
    public bool IsUnused => PostCount == 0 && TrashedPostCount == 0;
}