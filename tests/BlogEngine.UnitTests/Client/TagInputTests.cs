using BlogEngine.Client.Components;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Component tests for <see cref="TagInput"/> (design 6.4, A4, T1.12): typing a name that matches an existing
/// tag yields that tag's casing, duplicates can't be added, and the keyboard commits and removes chips.
/// </summary>
public class TagInputTests
{
    /// <summary>Typing <c>c#</c> when <c>C#</c> exists adds the chip <c>C#</c>, even before suggestions load.</summary>
    [Test]
    public async Task TypingExistingTagInOtherCasing_AddsOriginalCasing()
    {
        await using var context = CreateContext("C#", "Blazor");
        List<string>? changed = null;
        var cut = Render(context, [], tags => changed = tags);

        await TypeAsync(cut, "c#");
        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(Chips(cut)).IsEquivalentTo(["C#"]);
        await Assert.That(changed).IsEquivalentTo(["C#"]);
    }

    /// <summary>A tag that is already a chip, in any casing, is refused with a message.</summary>
    [Test]
    public async Task TypingDuplicate_IsRejected()
    {
        await using var context = CreateContext("C#");
        List<string>? changed = null;
        var cut = Render(context, ["C#"], tags => changed = tags);

        await TypeAsync(cut, "c#");
        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(Chips(cut)).IsEquivalentTo(["C#"]);
        await Assert.That(changed).IsNull();
        await Assert.That(cut.Find("[role=alert]").TextContent).IsEqualTo("'C#' is already added.");
    }

    /// <summary>A new tag keeps what the author typed.</summary>
    [Test]
    public async Task TypingNewTag_AddsIt()
    {
        await using var context = CreateContext("C#");
        var cut = Render(context, [], _ => { });

        await TypeAsync(cut, "Aspire");
        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Tab" });

        await Assert.That(Chips(cut)).IsEquivalentTo(["Aspire"]);
        await Assert.That(cut.Find("input").GetAttribute("value")).IsEqualTo(string.Empty);
    }

    /// <summary>Pasting a comma-separated list adds every complete name and leaves the last one to keep typing.</summary>
    [Test]
    public async Task PastingCommaList_AddsEachTag()
    {
        await using var context = CreateContext("C#");
        var cut = Render(context, [], _ => { });

        await TypeAsync(cut, "c#, Blazor, Aspi");

        await Assert.That(Chips(cut)).IsEquivalentTo(["C#", "Blazor"]);
        await Assert.That(cut.Find("input").GetAttribute("value")).IsEqualTo("Aspi");
    }

    /// <summary>The suggestions can be chosen with the arrow keys.</summary>
    [Test]
    public async Task ArrowDownAndEnter_CommitsSuggestion()
    {
        await using var context = CreateContext("Blazor", "Blazilla");
        var cut = Render(context, [], _ => { });

        await TypeAsync(cut, "bla");
        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(Chips(cut)).Count().IsEqualTo(1);
        await Assert.That(Chips(cut)[0]).StartsWith("Bla");
    }

    /// <summary>Backspace in an empty box removes the last chip.</summary>
    [Test]
    public async Task BackspaceInEmptyBox_RemovesLastChip()
    {
        await using var context = CreateContext();
        List<string>? changed = null;
        var cut = Render(context, ["one", "two"], tags => changed = tags);

        await cut.Find("input").KeyDownAsync(new KeyboardEventArgs { Key = "Backspace" });

        await Assert.That(Chips(cut)).IsEquivalentTo(["one"]);
        await Assert.That(changed).IsEquivalentTo(["one"]);
    }

    private static BunitContext CreateContext(params string[] existingTags)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ITagService>(new FakeTagService(existingTags));
        return context;
    }

    private static IRenderedComponent<TagInput> Render(BunitContext context, List<string> tags, Action<List<string>> onChanged)
    {
        return context.Render<TagInput>(parameters => parameters
            .Add(p => p.Tags, tags)
            .Add(p => p.TagsChanged, onChanged)
            .Add(p => p.SearchDelayMilliseconds, 0));
    }

    private static Task TypeAsync(IRenderedComponent<TagInput> cut, string text)
    {
        return cut.Find("input").InputAsync(new ChangeEventArgs { Value = text });
    }

    private static List<string> Chips(IRenderedComponent<TagInput> cut)
    {
        return [.. cut.FindAll("li.badge > span").Select(chip => chip.TextContent)];
    }

    /// <summary>Tag autocomplete over a fixed list, matching like the server (case-insensitive contains).</summary>
    private sealed class FakeTagService(IEnumerable<string> names) : ITagService
    {
        private readonly List<TagDto> _tags = [.. names.Select((name, i) => new TagDto { Id = i + 1, Name = name, Slug = TagNormalizer.ToSlug(name) })];

        public Task<IReadOnlyList<TagDto>> SearchAsync(string? search, CancellationToken cancellationToken = default)
        {
            var term = TagNormalizer.ToNormalizedName(search);
            IReadOnlyList<TagDto> matches = [.. _tags.Where(t => TagNormalizer.ToNormalizedName(t.Name).Contains(term, StringComparison.Ordinal))];
            return Task.FromResult(matches);
        }
    }
}
