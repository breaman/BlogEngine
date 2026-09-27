using BlogEngine.Server.Services.Public;

namespace BlogEngine.UnitTests.Server.Public;

/// <summary>Tests <see cref="SearchQuery"/>: how a reader's search is cleaned up before it is run (P9, T4.9).</summary>
public class SearchQueryTests
{
    /// <summary>Whitespace is collapsed for display, and repeated words (in any case) are searched once.</summary>
    [Test]
    public async Task Parse_CollapsesWhitespace_AndDeduplicatesTerms()
    {
        var query = SearchQuery.Parse("  Blazor \t forms   blazor ")!;

        await Assert.That(query.Text).IsEqualTo("Blazor forms blazor");
        await Assert.That(query.Terms).IsEquivalentTo(["Blazor", "forms"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>Nothing but whitespace is no search at all.</summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   \n ")]
    public async Task Parse_Blank_ReturnsNull(string? value)
    {
        await Assert.That(SearchQuery.Parse(value)).IsNull();
    }

    /// <summary>Long searches are cut to the length and word limits.</summary>
    [Test]
    public async Task Parse_LimitsLengthAndTerms()
    {
        var longText = SearchQuery.Parse(new string('x', 500))!;
        var manyWords = SearchQuery.Parse(string.Join(' ', Enumerable.Range(1, 20).Select(i => $"w{i}")))!;

        await Assert.That(longText.Text.Length).IsEqualTo(SearchQuery.MaxLength);
        await Assert.That(manyWords.Terms.Count).IsEqualTo(SearchQuery.MaxTerms);
        await Assert.That(manyWords.Terms[0]).IsEqualTo("w1");
    }

    /// <summary>A title matches when it contains every term, ignoring case.</summary>
    [Test]
    [Arguments("Building forms in Blazor", true)]
    [Arguments("BLAZOR FORMS", true)]
    [Arguments("Blazor tips", false)]
    public async Task MatchesAllIn_RequiresEveryTerm(string title, bool expected)
    {
        await Assert.That(SearchQuery.Parse("blazor forms")!.MatchesAllIn(title)).IsEqualTo(expected);
    }
}