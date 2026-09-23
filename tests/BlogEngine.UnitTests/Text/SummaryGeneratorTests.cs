using BlogEngine.Shared.Text;

namespace BlogEngine.UnitTests.Text;

/// <summary>
/// Tests <see cref="SummaryGenerator"/>: plain prose only, cut at a word boundary near 160 characters.
/// </summary>
public class SummaryGeneratorTests
{
    /// <summary>Short prose is returned as-is.</summary>
    [Test]
    public async Task FromMarkdown_ReturnsShortProseUnchanged()
    {
        await Assert.That(SummaryGenerator.FromMarkdown("A short post.")).IsEqualTo("A short post.");
    }

    /// <summary>Inline Markdown is reduced to its text.</summary>
    [Test]
    public async Task FromMarkdown_StripsInlineMarkup()
    {
        const string markdown = "Some **bold**, _italic_ and [linked](https://example.com) text with `code`.";

        await Assert.That(SummaryGenerator.FromMarkdown(markdown))
            .IsEqualTo("Some bold, italic and linked text with code.");
    }

    /// <summary>Headings, code blocks, images, tables and raw HTML are skipped.</summary>
    [Test]
    public async Task FromMarkdown_SkipsNonProseBlocks()
    {
        const string markdown = """
            # A Heading

            ```
            var code = true;
            ```

            ![An image](/media/abc/image.jpg)

            | a | b |
            |---|---|
            | c | d |

            <div>raw html</div>

            The first real paragraph.
            """;

        await Assert.That(SummaryGenerator.FromMarkdown(markdown)).IsEqualTo("The first real paragraph.");
    }

    /// <summary>Paragraphs and line breaks are joined with single spaces.</summary>
    [Test]
    public async Task FromMarkdown_JoinsParagraphs()
    {
        const string markdown = """
            First paragraph
            continues here.

            Second paragraph.
            """;

        await Assert.That(SummaryGenerator.FromMarkdown(markdown))
            .IsEqualTo("First paragraph continues here. Second paragraph.");
    }

    /// <summary>Long prose is cut at a word boundary and ends with an ellipsis, within the limit.</summary>
    [Test]
    public async Task FromMarkdown_TruncatesLongProseAtWordBoundary()
    {
        var prose = string.Join(' ', Enumerable.Range(1, 60).Select(i => $"word{i}"));

        var summary = SummaryGenerator.FromMarkdown(prose);
        var withoutEllipsis = summary.TrimEnd('…');

        await Assert.That(summary.Length).IsLessThanOrEqualTo(SummaryGenerator.DefaultMaxLength);
        await Assert.That(summary).EndsWith("…");
        await Assert.That(prose).StartsWith(withoutEllipsis + " ");
    }

    /// <summary>Trailing punctuation before the cut is dropped so the ellipsis reads naturally.</summary>
    [Test]
    public async Task FromMarkdown_DropsTrailingPunctuationBeforeEllipsis()
    {
        await Assert.That(SummaryGenerator.FromMarkdown("Hello, world, again and again", maxLength: 14))
            .IsEqualTo("Hello, world…");
    }

    /// <summary>A single word longer than the limit is hard-cut.</summary>
    [Test]
    public async Task FromMarkdown_HardCutsSingleLongWord()
    {
        var summary = SummaryGenerator.FromMarkdown(new string('x', 200));

        await Assert.That(summary).IsEqualTo(new string('x', SummaryGenerator.DefaultMaxLength - 1) + "…");
    }

    /// <summary>A post with no prose has no automatic summary.</summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("```\nonly code\n```")]
    [Arguments("## Only a heading")]
    public async Task FromMarkdown_ReturnsEmptyWithoutProse(string? markdown)
    {
        await Assert.That(SummaryGenerator.FromMarkdown(markdown)).IsEmpty();
    }
}