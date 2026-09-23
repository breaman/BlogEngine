using BlogEngine.Shared.Markdown;

namespace BlogEngine.Shared.Text;

/// <summary>
/// Word count and estimated reading time for a post (design 6.2, A7).
/// </summary>
/// <remarks>
/// Counts only the words a reader reads. Code blocks, raw HTML, image syntax, link URLs and Markdown
/// punctuation are ignored, so a code-heavy post isn't inflated by its listings.
/// </remarks>
/// <example>
/// <code>
/// var stats = ReadingTime.Calculate(post.ContentMarkdown);
/// post.WordCount = stats.WordCount;
/// post.ReadingMinutes = stats.ReadingMinutes;
/// </code>
/// </example>
public static class ReadingTime
{
    /// <summary>
    /// Average adult silent reading speed for non-fiction, in words per minute. Commonly quoted figures
    /// range from 200 to 265; this sits in the middle.
    /// </summary>
    public const int WordsPerMinute = 225;

    /// <summary>Counts the readable words in post Markdown and estimates the reading time.</summary>
    /// <param name="markdown">Post Markdown source.</param>
    public static ReadingStats Calculate(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new ReadingStats(0, 0);
        }

        var document = BlogMarkdownPipeline.Default.ParsePost(markdown);
        var wordCount = CountWords(MarkdownPlainText.ReadableText(document));

        return new ReadingStats(wordCount, MinutesFor(wordCount));
    }

    /// <summary>
    /// Reading minutes for a word count, rounded up so any content reads as at least one minute. Zero words
    /// is zero minutes.
    /// </summary>
    public static int MinutesFor(int wordCount)
    {
        return wordCount <= 0
            ? 0
            : (int)Math.Ceiling(wordCount / (double)WordsPerMinute);
    }

    /// <summary>
    /// Counts whitespace-separated tokens that contain at least one letter or digit, so stray punctuation
    /// such as a lone dash isn't counted and "well-known" counts once.
    /// </summary>
    internal static int CountWords(ReadOnlySpan<char> text)
    {
        var count = 0;
        var inToken = false;
        var tokenHasLetterOrDigit = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (inToken && tokenHasLetterOrDigit)
                {
                    count++;
                }

                inToken = false;
                tokenHasLetterOrDigit = false;
                continue;
            }

            inToken = true;
            tokenHasLetterOrDigit |= char.IsLetterOrDigit(c);
        }

        if (inToken && tokenHasLetterOrDigit)
        {
            count++;
        }

        return count;
    }
}

/// <summary>Result of <see cref="ReadingTime.Calculate"/>.</summary>
/// <param name="WordCount">Readable words in the post.</param>
/// <param name="ReadingMinutes">Estimated minutes to read; at least 1 for any non-empty post.</param>
public readonly record struct ReadingStats(int WordCount, int ReadingMinutes);