namespace BlogEngine.Shared.Contracts;

/// <summary>
/// A media library image chosen for a post outside its content: the cover (design 6.2, A15) or the social image
/// (A16). Carries what the editor needs to show a thumbnail.
/// </summary>
/// <param name="Id">The media item id.</param>
/// <param name="Url">Public URL of the current version, with the cache-busting <c>?v=</c>.</param>
/// <param name="AltText">The library's alt text.</param>
/// <param name="Width">Width of the current version, in pixels.</param>
/// <param name="Height">Height of the current version, in pixels.</param>
public sealed record PostImageDto(int Id, string Url, string AltText, int Width, int Height)
{
    /// <summary>The image as the editor shows it, from a library item.</summary>
    public static PostImageDto From(MediaItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new PostImageDto(item.Id, item.Url, item.AltText, item.Width, item.Height);
    }
}