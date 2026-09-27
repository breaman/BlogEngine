using BlogEngine.Client.Components;
using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Component tests for <see cref="MarkdownPreview"/> (T1.11): it renders with the shared pipeline in the
/// component itself and highlights code blocks through editor.js; library images are resolved through
/// <see cref="MediaLookupCache"/> (T2.7).
/// </summary>
public class MarkdownPreviewTests
{
    /// <summary>The Markdown is rendered locally, with source lines for scroll sync.</summary>
    [Test]
    public async Task RendersMarkdownWithSourceLines()
    {
        await using var context = CreateContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = context.Render<MarkdownPreview>(parameters => parameters.Add(p => p.Markdown, "# Hello\n\nWorld"));

        await Assert.That(cut.Find("h1").GetAttribute("data-line")).IsEqualTo("0");
        await Assert.That(cut.Find("p").TextContent).IsEqualTo("World");
    }

    /// <summary>After rendering, code blocks are handed to highlight.js.</summary>
    [Test]
    public async Task HighlightsAfterRender()
    {
        await using var context = CreateContext();
        var module = context.JSInterop.SetupModule("./js/editor.js");
        var highlight = module.SetupVoid("highlight", _ => true);

        context.Render<MarkdownPreview>(parameters => parameters.Add(p => p.Markdown, "```csharp\nvar x = 1;\n```"));

        await Assert.That(highlight.Invocations).Count().IsEqualTo(1);
    }

    /// <summary>An empty post shows a hint instead of nothing.</summary>
    [Test]
    public async Task EmptyMarkdown_ShowsPlaceholder()
    {
        await using var context = CreateContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = context.Render<MarkdownPreview>(parameters => parameters.Add(p => p.Markdown, " "));

        await Assert.That(cut.Find("p").TextContent).IsEqualTo("Nothing to preview yet.");
    }

    /// <summary>Library images are looked up once and rendered with their size and version, like the published post.</summary>
    [Test]
    public async Task ResolvesLibraryImages()
    {
        var media = new FakeMediaService(new MediaLookupItem(4, "ab12cd34ef56", "sunset.jpg", 800, 600, 2, "Sunset"));
        await using var context = CreateContext(media);
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = context.Render<MarkdownPreview>(parameters => parameters.Add(p => p.Markdown, "![](/media/ab12cd34ef56/sunset.jpg)"));
        cut.Render(parameters => parameters.Add(p => p.Markdown, "![](/media/ab12cd34ef56/sunset.jpg)\n\nMore text."));

        var image = cut.WaitForElement("figure.media-figure img");
        await Assert.That(image.GetAttribute("src")).IsEqualTo("/media/ab12cd34ef56/sunset.jpg?v=2");
        await Assert.That(image.GetAttribute("width")).IsEqualTo("800");
        await Assert.That(media.Lookups).Count().IsEqualTo(1);
    }

    /// <summary>A deleted image shows the placeholder instead of a broken image.</summary>
    [Test]
    public async Task MissingLibraryImage_ShowsPlaceholder()
    {
        await using var context = CreateContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = context.Render<MarkdownPreview>(parameters => parameters.Add(p => p.Markdown, "![](/media/zzzzzzzzzzzz/gone.jpg)"));

        await Assert.That(cut.WaitForElement(".media-missing-placeholder").TextContent).Contains("gone.jpg");
    }

    private static BunitContext CreateContext(FakeMediaService? media = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IMediaService>(media ?? new FakeMediaService());
        context.Services.AddScoped<MediaLookupCache>();
        return context;
    }
}