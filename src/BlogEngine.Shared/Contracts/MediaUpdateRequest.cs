namespace BlogEngine.Shared.Contracts;

/// <summary>
/// The editable metadata of a media item (<c>PUT /api/admin/media/{id}</c>, design 7.4, M4).
/// </summary>
public sealed class MediaUpdateRequest
{
    /// <summary>Alternative text; may be empty, but the editor prompts for it when inserting.</summary>
    public string AltText { get; set; } = string.Empty;

    /// <summary>Optional caption.</summary>
    public string? Caption { get; set; }
}
