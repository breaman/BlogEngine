using BlogEngine.Shared.Markdown;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Components;

/// <summary>
/// Live preview of post Markdown (design 10.1, 10.2, T1.11). Renders with the shared
/// <see cref="BlogMarkdownPipeline"/> in WebAssembly, with no server round trip, so the preview matches what
/// the server stores on save; then runs highlight.js on its code blocks.
/// </summary>
/// <remarks>
/// <para>
/// Top-level blocks carry their Markdown source line (<see cref="BlogMarkdownPipeline.RenderPostPreview"/>),
/// which <see cref="MarkdownEditor"/> uses to keep the preview scrolled to the part being edited.
/// </para>
/// <para>
/// The preview isn't sanitized, because HtmlSanitizer is server-only. That only matters for raw HTML the
/// author typed themselves, and published HTML is always sanitized on save, so a difference can appear only
/// for markup the sanitizer would strip (scripts, event handlers, iframes from other sites).
/// </para>
/// </remarks>
public sealed partial class MarkdownPreview : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<MarkdownPreview> Logger { get; set; } = default!;

    /// <summary>The Markdown to render.</summary>
    [Parameter] public string? Markdown { get; set; }

    /// <summary>Shown when there is no content yet.</summary>
    [Parameter] public string EmptyText { get; set; } = "Nothing to preview yet.";

    /// <summary>Raised after the preview's HTML changed and was highlighted, for example to re-sync scrolling.</summary>
    [Parameter] public EventCallback OnRendered { get; set; }

    private ElementReference _element;
    private IJSObjectReference? _module;
    private string? _renderedMarkdown;
    private string _html = string.Empty;
    private bool _htmlChanged;

    /// <summary>Re-renders the HTML only when the Markdown changed, so unrelated parent renders stay cheap.</summary>
    protected override void OnParametersSet()
    {
        if (Markdown == _renderedMarkdown)
        {
            return;
        }

        _renderedMarkdown = Markdown;
        _html = BlogMarkdownPipeline.Default.RenderPostPreview(Markdown).Html;
        _htmlChanged = true;
    }

    /// <summary>Skips diffing when nothing the preview shows has changed.</summary>
    protected override bool ShouldRender()
    {
        return _htmlChanged;
    }

    /// <summary>Highlights code blocks in the new HTML. Not called while prerendering, where there is no browser.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_htmlChanged)
        {
            return;
        }

        _htmlChanged = false;
        try
        {
            _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/editor.js");
            await _module.InvokeVoidAsync("highlight", _element);
        }
        catch (JSException ex)
        {
            // Highlighting is cosmetic; the preview is still correct without it.
            Logger.LogWarning(ex, "Highlighting the preview failed.");
        }

        await OnRendered.InvokeAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is being torn down; nothing to release.
            }
        }
    }
}
