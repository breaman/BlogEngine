namespace BlogEngine.Shared.Contracts;

/// <summary>
/// The outcome for one file of an upload (<c>POST /api/admin/media</c>, design 9.1). Each file gets its own
/// result, so one bad file doesn't stop the others.
/// </summary>
public sealed class MediaUploadResult
{
    /// <summary>The file name as uploaded.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The new library item; <see langword="null"/> when the file was rejected.</summary>
    public MediaItemDto? Item { get; set; }

    /// <summary>Why the file was rejected; <see langword="null"/> on success.</summary>
    public string? Error { get; set; }

    /// <summary>Existing items with the same content (a warning, not an error; design 9.1, M9).</summary>
    public List<MediaItemDto> Duplicates { get; set; } = [];

    /// <summary>Whether the file was stored.</summary>
    public bool Succeeded => Item is not null;
}