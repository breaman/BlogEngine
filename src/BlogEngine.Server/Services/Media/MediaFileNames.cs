using System.Security.Cryptography;

using BlogEngine.Shared.Common;
using BlogEngine.Shared.Text;

namespace BlogEngine.Server.Services.Media;

/// <summary>
/// Names used in media URLs (design 6.6): the random public id and the slugified file name.
/// </summary>
public static class MediaFileNames
{
    /// <summary>
    /// Lowercase letters and digits only: URLs are case-insensitive in practice, and the database's default collation
    /// would treat ids that differ only in case as duplicates. 36^12 ids leaves collisions vanishingly unlikely.
    /// </summary>
    private const string PublicIdAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Base name used when the uploaded name has nothing a slug can use.</summary>
    private const string FallbackName = "image";

    /// <summary>A new random public id of <see cref="FieldLengths.MediaPublicId"/> characters.</summary>
    public static string NewPublicId()
    {
        return RandomNumberGenerator.GetString(PublicIdAlphabet, FieldLengths.MediaPublicId);
    }

    /// <summary>
    /// The URL file name for an upload: the slug of the uploaded name without its extension, plus the extension of
    /// the detected format, so <c>IMG 0042.JPEG</c> becomes <c>img-0042.jpg</c> and a PNG named <c>photo.jpg</c>
    /// becomes <c>photo.png</c>.
    /// </summary>
    public static string FromUpload(string? uploadedName, string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(extension);

        // Browsers may send a full path (old Internet Explorer did); only the last segment is the name.
        var name = Path.GetFileNameWithoutExtension((uploadedName ?? string.Empty).Replace('\\', '/').Split('/')[^1]);
        var slug = SlugGenerator.Generate(name, FieldLengths.MediaFileName - extension.Length - 1);

        return $"{(slug.Length > 0 ? slug : FallbackName)}.{extension}";
    }
}
