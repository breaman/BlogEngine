using BlogEngine.Server.Services.Export;

namespace BlogEngine.UnitTests.Server.Export;

/// <summary>
/// Tests <see cref="FrontMatterWriter"/>: the YAML front matter of exported posts and pages (design 17, T4.24) reads back as
/// exactly the values written, whatever characters they contain.
/// </summary>
public class FrontMatterWriterTests
{
    /// <summary>The document is the front matter between fences, a blank line and the body with a final newline.</summary>
    [Test]
    public async Task ToDocument_FencesFrontMatterBeforeBody()
    {
        var document = new FrontMatterWriter()
            .Add("title", "Hello")
            .Add("draft", false)
            .Add("navOrder", 3)
            .ToDocument("First line.\r\nSecond line.");

        await Assert.That(document).IsEqualTo("---\ntitle: \"Hello\"\ndraft: false\nnavOrder: 3\n---\n\nFirst line.\nSecond line.\n");
    }

    /// <summary>An empty body leaves just the front matter.</summary>
    [Test]
    public async Task ToDocument_EmptyBody_HasOnlyFrontMatter()
    {
        await Assert.That(new FrontMatterWriter().Add("draft", true).ToDocument(string.Empty)).IsEqualTo("---\ndraft: true\n---\n");
    }

    /// <summary>Null values are left out rather than written as empty keys.</summary>
    [Test]
    public async Task Add_NullValues_AreSkipped()
    {
        var document = new FrontMatterWriter()
            .Add("title", "T")
            .Add("cover", (string?)null)
            .Add("lastmod", (DateTimeOffset?)null)
            .ToDocument(string.Empty);

        await Assert.That(document).IsEqualTo("---\ntitle: \"T\"\n---\n");
    }

    /// <summary>Timestamps are ISO 8601 with their offset, which Hugo and Jekyll read as dates.</summary>
    [Test]
    public async Task Add_Timestamp_IsIso8601WithOffset()
    {
        var document = new FrontMatterWriter()
            .Add("date", new DateTimeOffset(2026, 9, 22, 14, 30, 5, TimeSpan.FromHours(-5)))
            .ToDocument(string.Empty);

        await Assert.That(document).Contains("date: 2026-09-22T14:30:05-05:00\n");
    }

    /// <summary>Lists are block sequences of quoted strings; an empty list is <c>[]</c>.</summary>
    [Test]
    public async Task Add_List_WritesBlockSequenceOrEmptyFlow()
    {
        var document = new FrontMatterWriter()
            .Add("tags", ["C#", ".NET"])
            .Add("aliases", Array.Empty<string>())
            .ToDocument(string.Empty);

        await Assert.That(document).IsEqualTo("---\ntags:\n  - \"C#\"\n  - \".NET\"\naliases: []\n---\n");
    }

    /// <summary>
    /// Characters YAML treats specially are escaped inside double quotes, so values that look like other types or
    /// indicators stay strings.
    /// </summary>
    [Test]
    [Arguments("plain", "\"plain\"")]
    [Arguments("C#: \"tips\" & tricks", "\"C#: \\\"tips\\\" & tricks\"")]
    [Arguments(@"C:\path", "\"C:\\\\path\"")]
    [Arguments("two\nlines\ttab\rreturn", "\"two\\nlines\\ttab\\rreturn\"")]
    [Arguments("yes", "\"yes\"")]
    [Arguments("- not a list", "\"- not a list\"")]
    [Arguments("bell\u0007", "\"bell\\u0007\"")]
    [Arguments("line\u2028separator", "\"line\\u2028separator\"")]
    [Arguments("héllo 日本", "\"héllo 日本\"")]
    public async Task Quote_EscapesYamlSpecialCharacters(string value, string expected)
    {
        await Assert.That(FrontMatterWriter.Quote(value)).IsEqualTo(expected);
    }
}
