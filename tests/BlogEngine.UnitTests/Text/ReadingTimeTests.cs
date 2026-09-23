using BlogEngine.Shared.Text;

namespace BlogEngine.UnitTests.Text;

/// <summary>
/// Tests <see cref="ReadingTime"/>: only readable words count, and minutes round up.
/// </summary>
public class ReadingTimeTests
{
    /// <summary>No content means no words and no reading time.</summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   \n\n  ")]
    public async Task Calculate_ReturnsZeroForEmptyContent(string? markdown)
    {
        await Assert.That(ReadingTime.Calculate(markdown)).IsEqualTo(new ReadingStats(0, 0));
    }

    /// <summary>A very short post still reads as one minute.</summary>
    [Test]
    public async Task Calculate_ShortPostTakesOneMinute()
    {
        await Assert.That(ReadingTime.Calculate("Hello world.")).IsEqualTo(new ReadingStats(2, 1));
    }

    /// <summary>Plain prose is counted word by word, and minutes round up past each full minute.</summary>
    [Test]
    [Arguments(ReadingTime.WordsPerMinute, 1)]
    [Arguments(ReadingTime.WordsPerMinute + 1, 2)]
    [Arguments(ReadingTime.WordsPerMinute * 2, 2)]
    [Arguments(1000, 5)]
    public async Task Calculate_PlainText(int words, int expectedMinutes)
    {
        var markdown = string.Join(' ', Enumerable.Repeat("word", words));

        await Assert.That(ReadingTime.Calculate(markdown)).IsEqualTo(new ReadingStats(words, expectedMinutes));
    }

    /// <summary>Fenced and indented code blocks don't count, however long they are.</summary>
    [Test]
    public async Task Calculate_IgnoresCodeBlocks()
    {
        var code = string.Join('\n', Enumerable.Repeat("var value = Compute(input, options);", 200));
        var markdown = $"""
            Here is how to compute the value in C#.

            ```csharp
            {code}
            ```

            And the indented version:

                var other = Compute(input);
                return other;
            """;

        var stats = ReadingTime.Calculate(markdown);

        await Assert.That(stats).IsEqualTo(new ReadingStats(13, 1));
    }

    /// <summary>
    /// Markdown syntax, link URLs, images and raw HTML don't count; heading, emphasis, link and inline code
    /// text does.
    /// </summary>
    [Test]
    public async Task Calculate_IgnoresMarkdownSyntax()
    {
        const string markdown = """
            # Title

            Some **bold** and [a link](https://example.com/a/very/long/url) with `inline code`.

            ![alt text that is not read](/media/abc/image.jpg){.img-medium}

            <div class="note">raw html block</div>
            """;

        await Assert.That(ReadingTime.Calculate(markdown).WordCount).IsEqualTo(9);
    }

    /// <summary>List items, quotes and table cells are read, so they count.</summary>
    [Test]
    public async Task Calculate_CountsListsQuotesAndTables()
    {
        const string markdown = """
            - one
            - two

            > three

            | four | five |
            |------|------|
            | six  | seven |
            """;

        await Assert.That(ReadingTime.Calculate(markdown).WordCount).IsEqualTo(7);
    }

    /// <summary>Tokens without letters or digits, such as a spaced dash, aren't words.</summary>
    [Test]
    public async Task Calculate_IgnoresPunctuationOnlyTokens()
    {
        await Assert.That(ReadingTime.Calculate("Hello — world -- again").WordCount).IsEqualTo(3);
    }

    /// <summary>Minutes are derived from the word count alone.</summary>
    [Test]
    [Arguments(0, 0)]
    [Arguments(-5, 0)]
    [Arguments(1, 1)]
    [Arguments(ReadingTime.WordsPerMinute * 3, 3)]
    public async Task MinutesFor_RoundsUp(int words, int expected)
    {
        await Assert.That(ReadingTime.MinutesFor(words)).IsEqualTo(expected);
    }
}