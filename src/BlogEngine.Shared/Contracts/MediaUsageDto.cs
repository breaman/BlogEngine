namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A post that uses a media item, for "Used in N posts" (design 9.6).
/// </summary>
public sealed class MediaUsageDto
{
    /// <summary>The post id.</summary>
    public int PostId { get; set; }

    /// <summary>The post title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Whether the post is in the trash (it still counts: restoring it would bring the image back).</summary>
    public bool IsInTrash { get; set; }
}