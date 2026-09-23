using System.Globalization;
using System.Text;

using BlogEngine.Shared.Common;

namespace BlogEngine.Shared.Text;

/// <summary>
/// Cleans, validates and normalizes tag names, and builds tag slugs (design 6.4).
/// </summary>
/// <remarks>
/// Lives in Shared so the tag input can show "already exists" and block duplicates before saving. The
/// server uses the same rules, and the unique index on <c>NormalizedName</c> is the final guard.
/// </remarks>
/// <example>
/// <code>
/// var result = TagNormalizer.Normalize(" c# ");
/// // result.Name == "c#", result.NormalizedName == "C#"
/// var slug = TagNormalizer.ToSlug("C#"); // "csharp"
/// </code>
/// </example>
public static class TagNormalizer
{
    /// <summary>Longest allowed tag name, after cleaning.</summary>
    public const int MaxLength = FieldLengths.TagName;

    /// <summary>Prefix of the fallback slug used when a name has no ASCII letters or digits at all.</summary>
    private const string FallbackSlugPrefix = "tag-";

    /// <summary>
    /// Cleans and validates a tag name as typed by the author.
    /// </summary>
    /// <param name="name">Raw input from the tag chip editor.</param>
    /// <returns>The cleaned display name and duplicate-detection key, or the reason the name is rejected.</returns>
    public static TagNameResult Normalize(string? name)
    {
        var cleaned = Clean(name);

        var error = cleaned switch
        {
            "" => "Tag names can't be empty.",
            { Length: > MaxLength } => $"Tag names can be at most {MaxLength} characters.",
            _ when cleaned.AsSpan().IndexOfAny(',', ';') >= 0 => "Tag names can't contain commas or semicolons.",
            _ => null
        };

        return error is null
            ? new TagNameResult(cleaned, ToNormalizedName(cleaned), null)
            : new TagNameResult(cleaned, string.Empty, error);
    }

    /// <summary>
    /// Produces the display form: NFKC-normalized, invisible characters removed, whitespace runs collapsed
    /// to one space, and trimmed. Does not validate.
    /// </summary>
    /// <remarks>
    /// NFKC runs first so that look-alikes it folds (full-width <c>Ｃ＃</c>, a non-breaking space, the
    /// decomposed form of <c>é</c>) are cleaned and compared exactly like their plain equivalents.
    /// Zero-width characters are removed because they would otherwise create tags that look identical
    /// but never collide.
    /// </remarks>
    public static string Clean(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var normalized = UnicodeText.SafeNormalize(name, NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;

        foreach (var c in normalized)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
            }
            else if (!UnicodeText.IsInvisible(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// The duplicate-detection key: the cleaned name in invariant upper case, so <c>C#</c>, <c>c#</c> and
    /// <c> c# </c> all map to <c>C#</c>.
    /// </summary>
    public static string ToNormalizedName(string? name)
    {
        return Clean(name).ToUpperInvariant();
    }

    /// <summary>
    /// Builds a tag slug. Symbols that carry meaning in tech tag names are spelled out before slugifying,
    /// so <c>C#</c> (<c>csharp</c>) doesn't collide with <c>C</c> (<c>c</c>).
    /// </summary>
    /// <remarks>
    /// Mappings: <c>#</c> → <c>sharp</c>, <c>+</c> → <c>plus</c>, a leading <c>.</c> → <c>dot</c>, and
    /// <c>&amp;</c> → <c>and</c> (as a separate word, so <c>R&amp;D</c> becomes <c>r-and-d</c>). A name with
    /// no ASCII letters or digits (for example <c>日本語</c>) gets a stable hash-based slug such as
    /// <c>tag-1a2b3c4d</c>. The result can still collide with another tag's slug; add a suffix with
    /// <see cref="SlugGenerator.MakeUnique"/> using <see cref="FieldLengths.TagSlug"/>.
    /// </remarks>
    public static string ToSlug(string? name)
    {
        var cleaned = Clean(name);
        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        var spelledOut = cleaned.StartsWith('.') ? "dot" + cleaned[1..] : cleaned;
        spelledOut = spelledOut
            .Replace("#", "sharp", StringComparison.Ordinal)
            .Replace("+", "plus", StringComparison.Ordinal)
            .Replace("&", " and ", StringComparison.Ordinal);

        var slug = SlugGenerator.Generate(spelledOut, FieldLengths.TagSlug);
        return slug.Length > 0
            ? slug
            : FallbackSlugPrefix + StableHash(ToNormalizedName(cleaned));
    }

    /// <summary>
    /// 32-bit FNV-1a hash as eight hex digits. Deterministic across processes and platforms (unlike
    /// <see cref="string.GetHashCode()"/>) and needs no cryptography APIs, so it runs the same in WebAssembly.
    /// </summary>
    private static string StableHash(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var c in value)
        {
            hash = (hash ^ c) * prime;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Result of <see cref="TagNormalizer.Normalize"/>.
/// </summary>
/// <param name="Name">The cleaned display form, returned even when invalid so the UI can echo it.</param>
/// <param name="NormalizedName">The duplicate-detection key; empty when the name is invalid.</param>
/// <param name="Error">Why the name was rejected, or <see langword="null"/> when it is valid.</param>
public sealed record TagNameResult(string Name, string NormalizedName, string? Error)
{
    /// <summary>Whether the name can be used as a tag.</summary>
    public bool IsValid => Error is null;
}