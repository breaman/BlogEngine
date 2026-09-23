using System.Text;

using BlogEngine.Shared.Markdown;

namespace BlogEngine.Shared.Text;

/// <summary>
/// Builds the automatic post summary used when the author leaves it blank (design 6.2, A12).
/// </summary>
/// <remarks>
/// The summary doubles as the default meta description, so it defaults to 160 characters, roughly where
/// search engines truncate. It is taken from prose paragraphs only; headings, code, tables and images are
/// skipped.
/// </remarks>
/// <example>
/// <code>
/// if (string.IsNullOrWhiteSpace(post.Summary))
/// {
///     post.Summary = SummaryGenerator.FromMarkdown(post.ContentMarkdown);
/// }
/// </code>
/// </example>
public static class SummaryGenerator
{
    /// <summary>Default summary length, matching the meta description length.</summary>
    public const int DefaultMaxLength = 160;

    /// <summary>Appended when the text is cut short.</summary>
    private const char Ellipsis = '…';

    /// <summary>Generates a plain-text summary from post Markdown.</summary>
    /// <param name="markdown">Post Markdown source.</param>
    /// <param name="maxLength">Longest summary to return, including the trailing ellipsis.</param>
    /// <returns>The summary, or an empty string when the post has no prose.</returns>
    public static string FromMarkdown(string? markdown, int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 2);
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var document = BlogMarkdownPipeline.Default.ParsePost(markdown);
        var text = CollapseWhitespace(MarkdownPlainText.ProseText(document));

        return Truncate(text, maxLength);
    }

    /// <summary>
    /// Cuts <paramref name="text"/> to at most <paramref name="maxLength"/> characters at a word boundary,
    /// ending with an ellipsis when anything was removed.
    /// </summary>
    internal static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        // Leave room for the ellipsis. A space at index maxLength - 1 still leaves a whole word before it.
        var limit = maxLength - 1;
        var boundary = text.LastIndexOf(' ', limit);
        var cut = boundary > 0 ? text[..boundary] : text[..limit];

        // "Hello, world," reads better as "Hello, world…" than "Hello, world,…".
        return cut.TrimEnd(' ', ',', ';', ':', '-', '–', '—') + Ellipsis;
    }

    /// <summary>Joins lines and collapses every whitespace run into a single space.</summary>
    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            pendingSpace = false;
            builder.Append(c);
        }

        return builder.ToString();
    }
}