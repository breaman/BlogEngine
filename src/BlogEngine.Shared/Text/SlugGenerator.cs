using System.Globalization;
using System.Text;

using BlogEngine.Shared.Common;

namespace BlogEngine.Shared.Text;

/// <summary>
/// Turns titles into URL slugs made of <c>[a-z0-9-]</c> (design 7.2).
/// </summary>
/// <remarks>
/// Lives in Shared so the admin editor (WebAssembly) can show the same slug the server will generate.
/// </remarks>
/// <example>
/// <code>
/// var slug = SlugGenerator.Generate("Crème Brûlée: A How-To"); // "creme-brulee-a-how-to"
/// var unique = SlugGenerator.MakeUnique(slug, takenSlugs.Contains); // "creme-brulee-a-how-to-2" if taken
/// </code>
/// </example>
public static class SlugGenerator
{
    /// <summary>Maximum length of a generated slug; long titles are cut at a word boundary.</summary>
    public const int DefaultMaxLength = 80;

    /// <summary>
    /// Latin letters that don't decompose into a base letter plus a diacritic, so stripping marks alone
    /// would drop them (for example <c>ß</c> or <c>ø</c>).
    /// </summary>
    private static readonly Dictionary<char, string> Transliterations = new()
    {
        ['ß'] = "ss",
        ['æ'] = "ae",
        ['Æ'] = "ae",
        ['œ'] = "oe",
        ['Œ'] = "oe",
        ['ø'] = "o",
        ['Ø'] = "o",
        ['đ'] = "d",
        ['Đ'] = "d",
        ['ð'] = "d",
        ['Ð'] = "d",
        ['þ'] = "th",
        ['Þ'] = "th",
        ['ł'] = "l",
        ['Ł'] = "l",
        ['ı'] = "i"
    };

    /// <summary>
    /// Generates a slug: lowercase ASCII letters and digits, with each run of anything else collapsed to
    /// a single hyphen, no leading or trailing hyphens, and at most <paramref name="maxLength"/> characters.
    /// </summary>
    /// <param name="text">The text to slugify, usually a title.</param>
    /// <param name="maxLength">Longest slug to return; longer results are cut at the last word boundary.</param>
    /// <returns>
    /// The slug, or an empty string when <paramref name="text"/> has no usable characters (for example a
    /// title written entirely in a non-Latin script). Callers must supply their own fallback.
    /// </returns>
    public static string Generate(string? text, int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Compatibility decomposition splits "é" into "e" + a combining accent, and also folds
        // full-width letters, ligatures ("ﬁ") and superscripts ("²") into plain ASCII.
        var decomposed = UnicodeText.SafeNormalize(text, NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingHyphen = false;

        foreach (var c in decomposed)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                AppendWord(builder, char.ToLowerInvariant(c), ref pendingHyphen);
            }
            else if (Transliterations.TryGetValue(c, out var replacement))
            {
                AppendWord(builder, replacement, ref pendingHyphen);
            }
            else if (UnicodeText.IsCombiningMark(c) || IsApostrophe(c))
            {
                // Diacritics vanish ("é" → "e") and apostrophes join words ("don't" → "dont").
            }
            else if (char.IsWhiteSpace(c) || !UnicodeText.IsInvisible(c))
            {
                // Any other visible character separates words. The hyphen is only written once the
                // next word starts, so runs collapse and no hyphen is ever leading or trailing.
                pendingHyphen = true;
            }
        }

        return Truncate(builder.ToString(), maxLength);
    }

    /// <summary>
    /// Appends a collision suffix (<c>-2</c>, <c>-3</c>, …), shortening <paramref name="slug"/> if needed so
    /// the result still fits in <paramref name="maxLength"/>.
    /// </summary>
    /// <param name="slug">The slug that is already taken.</param>
    /// <param name="number">The suffix number; 2 or greater, because the unsuffixed slug is "number 1".</param>
    /// <param name="maxLength">Column length the result must fit in.</param>
    public static string WithSuffix(string slug, int number, int maxLength = FieldLengths.Slug)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 2);

        var suffix = "-" + number.ToString(CultureInfo.InvariantCulture);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, suffix.Length + 1);

        var baseLength = Math.Min(slug.Length, maxLength - suffix.Length);
        return slug[..baseLength].TrimEnd('-') + suffix;
    }

    /// <summary>
    /// Returns <paramref name="slug"/> if it is free, otherwise the first free <c>-2</c>, <c>-3</c>, …
    /// variant.
    /// </summary>
    /// <remarks>
    /// Takes a predicate rather than querying anything itself so it works on the server and in the
    /// browser. On the server, load the candidates once (the slug itself and slugs starting with
    /// <c>slug + "-"</c>) into a set and pass <c>set.Contains</c>, instead of querying per candidate.
    /// The database's unique index remains the final guard against a concurrent save.
    /// </remarks>
    /// <param name="slug">The preferred slug.</param>
    /// <param name="isTaken">Returns <see langword="true"/> when a slug is already in use.</param>
    /// <param name="maxLength">Column length the result must fit in.</param>
    /// <exception cref="InvalidOperationException">No free slug was found in a sane number of attempts.</exception>
    public static string MakeUnique(string slug, Func<string, bool> isTaken, int maxLength = FieldLengths.Slug)
    {
        ArgumentException.ThrowIfNullOrEmpty(slug);
        ArgumentNullException.ThrowIfNull(isTaken);

        if (!isTaken(slug))
        {
            return slug;
        }

        // A bound keeps a predicate that always answers "taken" from looping forever.
        const int maxAttempts = 10_000;
        for (var number = 2; number <= maxAttempts; number++)
        {
            var candidate = WithSuffix(slug, number, maxLength);
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free slug found for '{slug}' after {maxAttempts} attempts.");
    }

    /// <summary>Appends a word character, writing a pending separator hyphen first.</summary>
    private static void AppendWord(StringBuilder builder, char c, ref bool pendingHyphen)
    {
        if (pendingHyphen && builder.Length > 0)
        {
            builder.Append('-');
        }

        pendingHyphen = false;
        builder.Append(c);
    }

    /// <summary>Appends transliterated word characters, writing a pending separator hyphen first.</summary>
    private static void AppendWord(StringBuilder builder, string text, ref bool pendingHyphen)
    {
        foreach (var c in text)
        {
            AppendWord(builder, c, ref pendingHyphen);
        }
    }

    /// <summary>Straight and typographic apostrophes, which should join words rather than split them.</summary>
    private static bool IsApostrophe(char c)
    {
        return c is '\'' or '’' or '‘' or 'ʼ';
    }

    /// <summary>Cuts a slug to <paramref name="maxLength"/>, preferring the last hyphen so no word is split.</summary>
    private static string Truncate(string slug, int maxLength)
    {
        if (slug.Length <= maxLength)
        {
            return slug;
        }

        // Searching back from index maxLength (inclusive) also accepts a hyphen that sits just past the
        // limit, which means the first maxLength characters already end on a whole word.
        var boundary = slug.LastIndexOf('-', maxLength);
        return boundary > 0
            ? slug[..boundary]
            : slug[..maxLength];
    }
}