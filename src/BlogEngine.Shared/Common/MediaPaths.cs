using System.Globalization;

namespace BlogEngine.Shared.Common;

/// <summary>
/// Public URLs of media library items (design 9.4): <c>/media/{publicId}/{fileName}</c>, with <c>?v={version}</c>
/// appended as a cache-buster where the version is known.
/// </summary>
public static class MediaPaths
{
    /// <summary>First path segment of every media URL.</summary>
    public const string Prefix = "/media";

    /// <summary>The unversioned path, as written in Markdown: <c>/media/ab12cd34ef56/sunset.jpg</c>.</summary>
    public static string Item(string publicId, string fileName)
    {
        return $"{Prefix}/{publicId}/{fileName}";
    }

    /// <summary>The path with the cache-busting version: <c>/media/ab12cd34ef56/sunset.jpg?v=3</c>.</summary>
    public static string Versioned(string publicId, string fileName, int version)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Item(publicId, fileName)}?v={version}");
    }

    /// <summary>
    /// A responsive rendition (design 9.4): <c>/media/ab12cd34ef56/sunset.jpg?w=640&amp;v=3</c>, with <c>&amp;f=webp</c>
    /// for the WebP copy.
    /// </summary>
    public static string Rendition(string publicId, string fileName, int version, int width, bool webp = false)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Item(publicId, fileName)}?w={width}&v={version}{(webp ? "&f=webp" : string.Empty)}");
    }
}