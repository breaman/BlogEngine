using BlogEngine.Shared.Text;

namespace BlogEngine.UnitTests.Text;

/// <summary>
/// Tests <see cref="SlugGenerator"/> against the slug rules in design 7.2.
/// </summary>
public class SlugGeneratorTests
{
    /// <summary>Text is lowercased and spaces become single hyphens.</summary>
    [Test]
    [Arguments("Hello World", "hello-world")]
    [Arguments("ALL CAPS TITLE", "all-caps-title")]
    [Arguments("Version 2 Released", "version-2-released")]
    public async Task Generate_LowercasesAndHyphenates(string title, string expected)
    {
        await Assert.That(SlugGenerator.Generate(title)).IsEqualTo(expected);
    }

    /// <summary>Diacritics are stripped, and letters that don't decompose are transliterated.</summary>
    [Test]
    [Arguments("Crème Brûlée", "creme-brulee")]
    [Arguments("naïve café", "naive-cafe")]
    [Arguments("Ångström", "angstrom")]
    [Arguments("Straße", "strasse")]
    [Arguments("Øresund Bridge", "oresund-bridge")]
    [Arguments("Łódź", "lodz")]
    [Arguments("Café", "cafe")]
    public async Task Generate_StripsDiacritics(string title, string expected)
    {
        await Assert.That(SlugGenerator.Generate(title)).IsEqualTo(expected);
    }

    /// <summary>Runs of punctuation collapse to one hyphen, and no hyphen is left at either end.</summary>
    [Test]
    [Arguments("Hello, World!", "hello-world")]
    [Arguments("C# & .NET: What's New?", "c-net-whats-new")]
    [Arguments("  --Leading and trailing--  ", "leading-and-trailing")]
    [Arguments("a...b///c", "a-b-c")]
    [Arguments("100% Pure", "100-pure")]
    [Arguments("Don’t Panic", "dont-panic")]
    [Arguments("tabs\tand\nnewlines", "tabs-and-newlines")]
    [Arguments("zero​width", "zerowidth")]
    public async Task Generate_CollapsesPunctuation(string title, string expected)
    {
        await Assert.That(SlugGenerator.Generate(title)).IsEqualTo(expected);
    }

    /// <summary>Compatibility forms (full-width letters, ligatures, superscripts) fold to plain ASCII.</summary>
    [Test]
    [Arguments("Ｈｅｌｌｏ", "hello")]
    [Arguments("ﬁle", "file")]
    [Arguments("x²", "x2")]
    public async Task Generate_FoldsCompatibilityCharacters(string title, string expected)
    {
        await Assert.That(SlugGenerator.Generate(title)).IsEqualTo(expected);
    }

    /// <summary>Input with nothing usable produces an empty slug for the caller to replace.</summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("\t\n")]
    [Arguments("!!!")]
    [Arguments("日本語")]
    public async Task Generate_ReturnsEmptyForUnusableInput(string? title)
    {
        await Assert.That(SlugGenerator.Generate(title)).IsEmpty();
    }

    /// <summary>A lone surrogate from a bad paste doesn't throw.</summary>
    [Test]
    public async Task Generate_ToleratesLoneSurrogates()
    {
        await Assert.That(SlugGenerator.Generate("bad\uD800paste")).IsEqualTo("bad-paste");
    }

    /// <summary>Long titles are cut at the last word boundary within 80 characters.</summary>
    [Test]
    public async Task Generate_TruncatesLongTitlesAtWordBoundary()
    {
        var title = string.Join(' ', Enumerable.Repeat("word", 30));

        var slug = SlugGenerator.Generate(title);

        await Assert.That(slug).IsEqualTo(string.Join('-', Enumerable.Repeat("word", 16)));
        await Assert.That(slug.Length).IsLessThanOrEqualTo(SlugGenerator.DefaultMaxLength);
    }

    /// <summary>A word that ends exactly at the limit is kept whole.</summary>
    [Test]
    public async Task Generate_KeepsWordEndingExactlyAtLimit()
    {
        var firstWord = new string('x', SlugGenerator.DefaultMaxLength);

        await Assert.That(SlugGenerator.Generate(firstWord + " more")).IsEqualTo(firstWord);
    }

    /// <summary>A single word longer than the limit is hard-cut, since there is no boundary to use.</summary>
    [Test]
    public async Task Generate_HardCutsSingleLongWord()
    {
        var slug = SlugGenerator.Generate(new string('x', 100) + " more");

        await Assert.That(slug).IsEqualTo(new string('x', SlugGenerator.DefaultMaxLength));
    }

    /// <summary>A custom maximum length is honored.</summary>
    [Test]
    public async Task Generate_HonorsCustomMaxLength()
    {
        await Assert.That(SlugGenerator.Generate("one two three four", maxLength: 10)).IsEqualTo("one-two");
    }

    /// <summary>Collision suffixes are appended with a hyphen.</summary>
    [Test]
    [Arguments(2, "my-post-2")]
    [Arguments(3, "my-post-3")]
    [Arguments(12, "my-post-12")]
    public async Task WithSuffix_AppendsNumber(int number, string expected)
    {
        await Assert.That(SlugGenerator.WithSuffix("my-post", number)).IsEqualTo(expected);
    }

    /// <summary>The base is shortened so the suffixed slug still fits, without leaving a double hyphen.</summary>
    [Test]
    [Arguments("abcdefghij", 2, 10, "abcdefgh-2")]
    [Arguments("abcdef-hij", 2, 9, "abcdef-2")]
    public async Task WithSuffix_ShortensBaseToFit(string slug, int number, int maxLength, string expected)
    {
        await Assert.That(SlugGenerator.WithSuffix(slug, number, maxLength)).IsEqualTo(expected);
    }

    /// <summary>Suffix 1 would be the unsuffixed slug, so numbering starts at 2.</summary>
    [Test]
    public async Task WithSuffix_RejectsNumbersBelowTwo()
    {
        await Assert.That(() => SlugGenerator.WithSuffix("my-post", 1)).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>A free slug is returned unchanged.</summary>
    [Test]
    public async Task MakeUnique_ReturnsFreeSlugUnchanged()
    {
        await Assert.That(SlugGenerator.MakeUnique("my-post", _ => false)).IsEqualTo("my-post");
    }

    /// <summary>A taken slug gets the first free suffix.</summary>
    [Test]
    public async Task MakeUnique_AppendsFirstFreeSuffix()
    {
        HashSet<string> taken = ["my-post", "my-post-2", "my-post-3"];

        await Assert.That(SlugGenerator.MakeUnique("my-post", taken.Contains)).IsEqualTo("my-post-4");
    }

    /// <summary>A gap in the existing suffixes is reused.</summary>
    [Test]
    public async Task MakeUnique_ReusesGapInSuffixes()
    {
        HashSet<string> taken = ["my-post", "my-post-3"];

        await Assert.That(SlugGenerator.MakeUnique("my-post", taken.Contains)).IsEqualTo("my-post-2");
    }

    /// <summary>A predicate that never frees a slug fails instead of looping forever.</summary>
    [Test]
    public async Task MakeUnique_ThrowsWhenNothingIsFree()
    {
        await Assert.That(() => SlugGenerator.MakeUnique("my-post", _ => true)).Throws<InvalidOperationException>();
    }
}