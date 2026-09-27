using System.Globalization;

namespace BlogEngine.Server.Storage;

/// <summary>
/// The storage key layout for media items (design 9.3): <c>{publicId}/original.{ext}</c> for the upload,
/// <c>{publicId}/v{version}/current.{ext}</c> for each edited version, and <c>{publicId}/v{version}/{width}.{ext}</c>
/// for the responsive renditions of a version.
/// </summary>
public static class MediaStorageKeys
{
    /// <summary>Key of the original upload.</summary>
    public static string Original(string publicId, string extension)
    {
        return $"{publicId}/original.{extension}";
    }

    /// <summary>Key of an edited version.</summary>
    public static string Version(string publicId, int version, string extension)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{publicId}/v{version}/current.{extension}");
    }

    /// <summary>Key of a responsive rendition of a version (design 9.4), such as <c>ab12cd34ef56/v3/640.webp</c>.</summary>
    public static string Rendition(string publicId, int version, int width, string extension)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{publicId}/v{version}/{width}.{extension}");
    }

    /// <summary>Prefix that covers every object of a media item, for deleting all of them at once.</summary>
    public static string ItemPrefix(string publicId)
    {
        return $"{publicId}/";
    }
}