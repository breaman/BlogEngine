namespace BlogEngine.Shared.Contracts;

/// <summary>
/// What the Markdown renderer needs to know about a library image (design 9.4): its current size and version
/// for the <c>&lt;img&gt;</c> tag, and its alt text as a fallback.
/// </summary>
/// <param name="Id">The item id, for usage tracking.</param>
/// <param name="PublicId">Short random id used in the URL.</param>
/// <param name="FileName">File name used in the URL.</param>
/// <param name="Width">Width of the current version, in pixels.</param>
/// <param name="Height">Height of the current version, in pixels.</param>
/// <param name="Version">Current version, appended as <c>?v=</c>.</param>
/// <param name="AltText">Library alt text, used when the Markdown gives none.</param>
public sealed record MediaLookupItem(int Id, string PublicId, string FileName, int Width, int Height, int Version, string AltText);
