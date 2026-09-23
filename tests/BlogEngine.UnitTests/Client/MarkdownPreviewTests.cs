using BlogEngine.Client.Components;

using Bunit;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Component tests for <see cref="MarkdownPreview"/> (T1.11): it renders with the shared pipeline in the
/// component itself and highlights code blocks through editor.js.
/// </summary>
public class MarkdownPreviewTests
{
    /// <summary>The Markdown is rendered locally, with source lines for scroll sync.</summary>
    [Test]
    public async Task RendersMarkdownWithSourceLines()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = context.Render<MarkdownPreview>(parameters => parameters.Add(p => p.Markdown, "# Hello\n\nWorld"));

        await Assert.That(cut.Find("h1").GetAttribute("data-line")).IsEqualTo("0");
        await Assert.That(cut.Find("p").TextContent).IsEqualTo("World");
    }

    /// <summary>After rendering, code blocks are handed to highlight.js.</summary>
    [Test]
    public async Task HighlightsAfterRender()
    {
        await using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./js/editor.js");
        var highlight = module.SetupVoid("highlight", _ => true);

        context.Render<MarkdownPreview>(parameters => parameters.Add(p => p.Markdown, "```csharp\nvar x = 1;\n```"));

        await Assert.That(highlight.Invocations).Count().IsEqualTo(1);
    }

    /// <summary>An empty post shows a hint instead of nothing.</summary>
    [Test]
    public async Task EmptyMarkdown_ShowsPlaceholder()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = context.Render<MarkdownPreview>(parameters => parameters.Add(p => p.Markdown, " "));

        await Assert.That(cut.Find("p").TextContent).IsEqualTo("Nothing to preview yet.");
    }
}
