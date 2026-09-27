using BlogEngine.Client.Components;
using BlogEngine.Shared.Contracts;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests <see cref="TagInputRules"/>, which decides what a committed tag becomes (design 6.4, A4, T1.12).
/// </summary>
public class TagInputRulesTests
{
    private static readonly TagDto[] Known = [new() { Id = 1, Name = "C#", Slug = "csharp" }, new() { Id = 2, Name = ".NET", Slug = "dotnet" }];

    /// <summary>Typing an existing tag in any casing gives that tag's original casing.</summary>
    [Test]
    [Arguments("c#")]
    [Arguments("C#")]
    [Arguments("  c#  ")]
    public async Task Resolve_ExistingTag_UsesOriginalCasing(string typed)
    {
        var result = TagInputRules.Resolve(typed, [], Known, maxTags: 20);

        await Assert.That(result.Name).IsEqualTo("C#");
        await Assert.That(result.MatchedExisting).IsTrue();
    }

    /// <summary>A new tag keeps the author's (cleaned) text.</summary>
    [Test]
    public async Task Resolve_NewTag_KeepsCleanedName()
    {
        var result = TagInputRules.Resolve("  Blazor   WebAssembly ", [], Known, maxTags: 20);

        await Assert.That(result.Name).IsEqualTo("Blazor WebAssembly");
        await Assert.That(result.MatchedExisting).IsFalse();
    }

    /// <summary>A chip that is already there, in any casing, can't be added again.</summary>
    [Test]
    public async Task Resolve_Duplicate_IsRejected()
    {
        var result = TagInputRules.Resolve("c#", ["C#"], Known, maxTags: 20);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Error).IsEqualTo("'C#' is already added.");
    }

    /// <summary><c>C#</c> and <c>C</c> are different tags.</summary>
    [Test]
    public async Task Resolve_SimilarButDifferentTag_IsAccepted()
    {
        var result = TagInputRules.Resolve("C", ["C#"], Known, maxTags: 20);

        await Assert.That(result.Name).IsEqualTo("C");
    }

    /// <summary>Invalid names get the normalizer's message.</summary>
    [Test]
    [Arguments("   ", "Tag names can't be empty.")]
    [Arguments("a;b", "Tag names can't contain commas or semicolons.")]
    public async Task Resolve_InvalidName_IsRejected(string typed, string error)
    {
        var result = TagInputRules.Resolve(typed, [], Known, maxTags: 20);

        await Assert.That(result.Error).IsEqualTo(error);
    }

    /// <summary>The tag limit applies before a new chip is added.</summary>
    [Test]
    public async Task Resolve_AtLimit_IsRejected()
    {
        var result = TagInputRules.Resolve("third", ["one", "two"], Known, maxTags: 2);

        await Assert.That(result.Error).IsEqualTo("A post can have at most 2 tags.");
    }
}