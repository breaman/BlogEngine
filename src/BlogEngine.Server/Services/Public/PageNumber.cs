using System.Globalization;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// Reads the <c>?page=</c> query value of public list pages (design 7.1).
/// </summary>
/// <remarks>
/// The value is bound as a string and parsed here, rather than bound as an <see cref="int"/>, so a malformed
/// value such as <c>?page=abc</c> becomes a 404 instead of a binding exception.
/// </remarks>
public static class PageNumber
{
    /// <summary>
    /// The 1-based page for <paramref name="value"/>: 1 when it's missing, otherwise a positive integer.
    /// Returns <see langword="false"/> for anything else, which the page answers with a 404.
    /// </summary>
    /// <example>
    /// <code>
    /// PageNumber.TryParse(null, out var page);  // true, page == 1
    /// PageNumber.TryParse("3", out page);       // true, page == 3
    /// PageNumber.TryParse("0", out page);       // false
    /// </code>
    /// </example>
    public static bool TryParse(string? value, out int page)
    {
        if (string.IsNullOrEmpty(value))
        {
            page = 1;
            return true;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1;
    }
}
