using System.Globalization;

namespace BlogEngine.Client.Services;

/// <summary>Formatting helpers for the media pages.</summary>
public static class MediaFormat
{
    /// <summary>A byte count for people, such as <c>24.5 MB</c> or <c>812 KB</c>.</summary>
    public static string Size(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
            >= 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0} KB"),
            _ => string.Create(CultureInfo.CurrentCulture, $"{bytes} bytes")
        };
    }
}
