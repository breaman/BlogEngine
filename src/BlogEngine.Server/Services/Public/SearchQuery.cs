using System.Globalization;

namespace BlogEngine.Server.Services.Public;

/// <summary>
/// A reader's search (design 15, P9) cleaned up for querying: the text as typed (trimmed, whitespace collapsed and
/// length-limited) and the distinct words to look for.
/// </summary>
/// <remarks>
/// Every word must appear somewhere in a post (title, summary or content), in any order, so "blazor forms" finds a
/// post about forms in Blazor. The limits keep a single request from turning into an expensive query.
/// </remarks>
/// <example>
/// <code>
/// SearchQuery.Parse("  Blazor   forms blazor "); // Text "Blazor forms blazor", Terms ["Blazor", "forms"]
/// SearchQuery.Parse("   ");                       // null: nothing to search for
/// </code>
/// </example>
public sealed record SearchQuery
{
    /// <summary>The most characters of a search that are used; the rest is ignored.</summary>
    public const int MaxLength = 100;

    /// <summary>The most words of a search that are matched; later words are ignored.</summary>
    public const int MaxTerms = 8;

    private SearchQuery(string text, IReadOnlyList<string> terms)
    {
        Text = text;
        Terms = terms;
    }

    /// <summary>The search as shown back to the reader.</summary>
    public string Text { get; }

    /// <summary>The distinct words (compared case-insensitively) that every result must contain.</summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>The query for the <c>?q=</c> value, or <see langword="null"/> when it has no words.</summary>
    public static SearchQuery? Parse(string? value)
    {
        var words = (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        var text = string.Join(' ', words);
        if (text.Length > MaxLength)
        {
            text = text[..MaxLength].TrimEnd();
            words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        var terms = words.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTerms).ToArray();
        return new SearchQuery(text, terms);
    }

    /// <summary>Whether <paramref name="title"/> contains every term, ignoring case; such posts are ranked first.</summary>
    public bool MatchesAllIn(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        return Terms.All(term => compare.IndexOf(title, term, CompareOptions.IgnoreCase) >= 0);
    }
}
