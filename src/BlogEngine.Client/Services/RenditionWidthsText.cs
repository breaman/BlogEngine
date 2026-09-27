using System.Globalization;

namespace BlogEngine.Client.Services;

/// <summary>
/// Converts the rendition widths setting (design 9.4, 13) between its list form and the text box on the settings page,
/// where the author types something like <c>320, 640, 960</c>.
/// </summary>
/// <remarks>
/// Only the shape is checked here (whole numbers separated by commas, semicolons or spaces, with an optional <c>px</c>).
/// Ranges and the "at least one width" rule stay in <c>SiteSettingsValidator</c>, so client and server report them the
/// same way.
/// </remarks>
/// <example>
/// <code>
/// RenditionWidthsText.TryParse("960, 320 640px", out var widths, out _); // widths: [320, 640, 960]
/// RenditionWidthsText.Format([320, 640]);                               // "320, 640"
/// </code>
/// </example>
public static class RenditionWidthsText
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>The widths as the text box shows them, such as <c>320, 640, 960</c>.</summary>
    public static string Format(IEnumerable<int> widths)
    {
        ArgumentNullException.ThrowIfNull(widths);

        return string.Join(", ", widths.Select(w => w.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Reads the widths typed by the author, sorted and without duplicates. Blank text gives an empty list (reported by
    /// the validator).
    /// </summary>
    /// <param name="text">The text box's contents.</param>
    /// <param name="widths">The widths, or an empty list when the text can't be read.</param>
    /// <param name="error">Why the text can't be read, or <see langword="null"/> when it can.</param>
    /// <returns>Whether every part of the text is a width.</returns>
    public static bool TryParse(string? text, out IReadOnlyList<int> widths, out string? error)
    {
        var parsed = new SortedSet<int>();
        foreach (var part in (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var number = part.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? part[..^2] : part;
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var width))
            {
                widths = [];
                error = $"'{part}' isn't a width. Enter whole numbers of pixels separated by commas, for example 320, 640, 960.";
                return false;
            }

            parsed.Add(width);
        }

        widths = [.. parsed];
        error = null;
        return true;
    }
}