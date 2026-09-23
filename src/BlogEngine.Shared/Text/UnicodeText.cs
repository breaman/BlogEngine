using System.Globalization;
using System.Text;

namespace BlogEngine.Shared.Text;

/// <summary>
/// Unicode helpers shared by the slug and tag utilities.
/// </summary>
internal static class UnicodeText
{
    /// <summary>
    /// Normalizes <paramref name="text"/> to <paramref name="form"/> without throwing on malformed input.
    /// </summary>
    /// <remarks>
    /// <see cref="string.Normalize(NormalizationForm)"/> throws <see cref="ArgumentException"/> when the
    /// string contains a lone surrogate. Titles and tag names come straight from user input, so a bad
    /// paste must not turn a save into a server error; lone surrogates become U+FFFD first.
    /// </remarks>
    public static string SafeNormalize(string text, NormalizationForm form)
    {
        return ReplaceLoneSurrogates(text).Normalize(form);
    }

    /// <summary>
    /// Whether <paramref name="c"/> is a combining mark (the diacritic half of a decomposed character).
    /// </summary>
    public static bool IsCombiningMark(char c)
    {
        return CharUnicodeInfo.GetUnicodeCategory(c)
            is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }

    /// <summary>
    /// Whether <paramref name="c"/> is an invisible formatting or control character, such as a zero-width
    /// space or a NUL, that should never contribute to a slug or tag name.
    /// </summary>
    public static bool IsInvisible(char c)
    {
        return CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.Control;
    }

    /// <summary>Replaces unpaired UTF-16 surrogates with U+FFFD, returning the original string if there are none.</summary>
    private static string ReplaceLoneSurrogates(string text)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var isValidPair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            if (isValidPair)
            {
                builder?.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                builder ??= new StringBuilder(text.Length).Append(text, 0, i);
                builder.Append('�');
                continue;
            }

            builder?.Append(c);
        }

        return builder?.ToString() ?? text;
    }
}