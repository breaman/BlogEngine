using BlogEngine.Shared.Common;
using BlogEngine.Shared.Text;

namespace BlogEngine.UnitTests.Text;

/// <summary>
/// Tests <see cref="TagNormalizer"/> against the tag rules in design 6.4.
/// </summary>
public class TagNormalizerTests
{
    /// <summary>Case, surrounding whitespace and full-width forms all resolve to the same tag.</summary>
    [Test]
    [Arguments("C#")]
    [Arguments("c#")]
    [Arguments(" c# ")]
    [Arguments("\tc#\n")]
    [Arguments("Ｃ＃")]
    public async Task Normalize_CSharpVariantsCollide(string input)
    {
        var result = TagNormalizer.Normalize(input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.NormalizedName).IsEqualTo("C#");
    }

    /// <summary><c>C#</c> and <c>C</c> are different tags with different slugs.</summary>
    [Test]
    public async Task Normalize_CSharpDiffersFromC()
    {
        await Assert.That(TagNormalizer.ToNormalizedName("C#")).IsNotEqualTo(TagNormalizer.ToNormalizedName("C"));
        await Assert.That(TagNormalizer.ToSlug("C#")).IsNotEqualTo(TagNormalizer.ToSlug("C"));
    }

    /// <summary>The display name keeps the author's casing but is trimmed and whitespace-collapsed.</summary>
    [Test]
    [Arguments("  Visual   Studio  ", "Visual Studio")]
    [Arguments("Visual Studio", "Visual Studio")]
    [Arguments("Zero​Width", "ZeroWidth")]
    [Arguments("ﬁle", "file")]
    public async Task Normalize_CleansDisplayName(string input, string expected)
    {
        var result = TagNormalizer.Normalize(input);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Name).IsEqualTo(expected);
    }

    /// <summary>Composed and decomposed forms of the same accented word are one tag.</summary>
    [Test]
    public async Task Normalize_ComposedAndDecomposedAccentsCollide()
    {
        var composed = TagNormalizer.Normalize("Café");
        var decomposed = TagNormalizer.Normalize("Café");

        await Assert.That(decomposed.NormalizedName).IsEqualTo(composed.NormalizedName);
        await Assert.That(decomposed.Name).IsEqualTo("Café");
    }

    /// <summary>Non-Latin scripts are kept and uppercased with invariant rules.</summary>
    [Test]
    [Arguments("λάμδα", "ΛΆΜΔΑ")]
    [Arguments("привет", "ПРИВЕТ")]
    [Arguments("日本語", "日本語")]
    public async Task Normalize_UppercasesUnicode(string input, string expected)
    {
        await Assert.That(TagNormalizer.Normalize(input).NormalizedName).IsEqualTo(expected);
    }

    /// <summary>Empty and whitespace-only names are rejected.</summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("​")]
    public async Task Normalize_RejectsEmptyNames(string? input)
    {
        var result = TagNormalizer.Normalize(input);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.NormalizedName).IsEmpty();
    }

    /// <summary>Commas and semicolons are input delimiters, so tag names can't contain them.</summary>
    [Test]
    [Arguments("a,b")]
    [Arguments("a;b")]
    [Arguments("a，b")]
    public async Task Normalize_RejectsDelimiters(string input)
    {
        await Assert.That(TagNormalizer.Normalize(input).IsValid).IsFalse();
    }

    /// <summary>The length limit applies after trimming, so surrounding spaces don't count.</summary>
    [Test]
    public async Task Normalize_EnforcesMaxLengthAfterCleaning()
    {
        var atLimit = new string('a', FieldLengths.TagName);

        await Assert.That(TagNormalizer.Normalize("  " + atLimit + "  ").IsValid).IsTrue();
        await Assert.That(TagNormalizer.Normalize(atLimit + "a").IsValid).IsFalse();
    }

    /// <summary>Meaningful symbols are spelled out before slugifying.</summary>
    [Test]
    [Arguments("C#", "csharp")]
    [Arguments("c#", "csharp")]
    [Arguments("F#", "fsharp")]
    [Arguments("C++", "cplusplus")]
    [Arguments(".NET", "dotnet")]
    [Arguments(".NET Core", "dotnet-core")]
    [Arguments("ASP.NET Core", "asp-net-core")]
    [Arguments("R&D", "r-and-d")]
    [Arguments("Node.js", "node-js")]
    [Arguments("Café", "cafe")]
    [Arguments(" Visual  Studio ", "visual-studio")]
    public async Task ToSlug_SpellsOutSymbols(string name, string expected)
    {
        await Assert.That(TagNormalizer.ToSlug(name)).IsEqualTo(expected);
    }

    /// <summary>A name with no ASCII letters or digits gets a stable, name-specific fallback slug.</summary>
    [Test]
    public async Task ToSlug_FallsBackToStableHashForNonLatinNames()
    {
        var slug = TagNormalizer.ToSlug("日本語");

        await Assert.That(slug).Matches("^tag-[0-9a-f]{8}$");
        await Assert.That(TagNormalizer.ToSlug(" 日本語 ")).IsEqualTo(slug);
        await Assert.That(TagNormalizer.ToSlug("中文")).IsNotEqualTo(slug);
    }

    /// <summary>Case-only variants of a non-Latin name share the fallback slug, like they share a tag.</summary>
    [Test]
    public async Task ToSlug_FallbackIgnoresCase()
    {
        await Assert.That(TagNormalizer.ToSlug("λάμδα")).IsEqualTo(TagNormalizer.ToSlug("ΛΆΜΔΑ"));
    }

    /// <summary>Expanded symbols never push a slug past the column length.</summary>
    [Test]
    public async Task ToSlug_FitsTagSlugColumn()
    {
        var slug = TagNormalizer.ToSlug(new string('#', FieldLengths.TagName));

        await Assert.That(slug.Length).IsLessThanOrEqualTo(FieldLengths.TagSlug);
    }
}