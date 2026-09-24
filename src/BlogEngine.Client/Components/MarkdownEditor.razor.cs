using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Components;

/// <summary>
/// Markdown editor with a formatting toolbar and a live preview (design 10.2, T1.10, T1.11). The editing
/// surface is CodeMirror 6 (<c>wwwroot/js/editor.js</c>, built from <c>scripts/editor.js</c>); the preview is
/// <see cref="MarkdownPreview"/>, rendered in .NET with the shared Markdown pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Changes are debounced by 200 ms in JavaScript before they reach .NET, so the preview and
/// <see cref="ValueChanged"/> fire once per pause in typing rather than on every keystroke. Call
/// <see cref="FlushAsync"/> before saving to make sure the bound value holds the latest text.
/// </para>
/// <para>
/// The layout switches between split, editor only and preview only on wide screens; on narrow screens the
/// panes become Write/Preview tabs. The preview follows the editor's scroll position using the source lines
/// Markdig records for each block.
/// </para>
/// <para>
/// Shortcuts: <c>Ctrl/Cmd+B</c> bold, <c>I</c> italic, <c>K</c> link, <c>`</c> code, <c>Shift+I</c> image
/// (<see cref="OnInsertImage"/> when set, otherwise an image template) and <c>S</c> (<see cref="OnSave"/>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;MarkdownEditor @ref="_editor" @bind-Value="Post.ContentMarkdown" OnSave="SaveAsync" OnBlur="AutosaveAsync" /&gt;
/// </code>
/// </example>
public sealed partial class MarkdownEditor : ComponentBase, IAsyncDisposable
{
    /// <summary>Toolbar buttons, grouped as they are shown; commands are implemented in editor.js.</summary>
    private static readonly IReadOnlyList<IReadOnlyList<ToolbarButton>> ToolbarGroups =
    [
        [
            new("bold", "bi-type-bold", "Bold (Ctrl+B)"),
            new("italic", "bi-type-italic", "Italic (Ctrl+I)"),
            new("code", "bi-code", "Code (Ctrl+`)")
        ],
        [
            new("heading", "bi-type-h2", "Heading"),
            new("quote", "bi-quote", "Quote"),
            new("bulletList", "bi-list-ul", "Bulleted list"),
            new("orderedList", "bi-list-ol", "Numbered list")
        ],
        [
            new("link", "bi-link-45deg", "Link (Ctrl+K)"),
            new("image", "bi-image", "Image (Ctrl+Shift+I)")
        ]
    ];

    /// <summary>The wide-screen layout choices.</summary>
    private static readonly IReadOnlyList<LayoutOption> LayoutOptions =
    [
        new(EditorLayout.Split, "bi-layout-split", "Editor and preview side by side"),
        new(EditorLayout.Editor, "bi-pencil-square", "Editor only"),
        new(EditorLayout.Preview, "bi-eye", "Preview only")
    ];

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<MarkdownEditor> Logger { get; set; } = default!;

    /// <summary>The Markdown being edited.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>Raised (debounced) when the author changes the text.</summary>
    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    /// <summary>Placeholder shown in an empty editor.</summary>
    [Parameter] public string Placeholder { get; set; } = "Write in Markdown…";

    /// <summary>Raised by <c>Ctrl/Cmd+S</c>, after the latest text has been reported through <see cref="ValueChanged"/>.</summary>
    [Parameter] public EventCallback OnSave { get; set; }

    /// <summary>Raised when the editor loses focus, after the latest text has been reported.</summary>
    [Parameter] public EventCallback OnBlur { get; set; }

    /// <summary>
    /// Raised by the image button and <c>Ctrl/Cmd+Shift+I</c> so the page can open the media picker, which then
    /// calls <see cref="InsertBlockAsync"/>. Without a handler, an image template is inserted.
    /// </summary>
    [Parameter] public EventCallback OnInsertImage { get; set; }

    private ElementReference _host;
    private ElementReference _previewPane;
    private IJSObjectReference? _module;
    private IJSObjectReference? _editor;
    private DotNetObjectReference<MarkdownEditor>? _selfReference;

    /// <summary>The text CodeMirror holds, as far as .NET knows; the preview renders this.</summary>
    private string _editorValue = string.Empty;

    /// <summary>The last <see cref="Value"/> received from the parent, to tell its own changes from echoes.</summary>
    private string? _lastValueParameter;

    private bool _initialized;
    private bool _pushValueToEditor;
    private bool _ready;
    private bool _disposed;
    private EditorLayout _layout = EditorLayout.Split;
    private NarrowTab _narrowTab = NarrowTab.Write;

    /// <summary>Formatting needs a live editor, and makes no sense while only the preview is shown.</summary>
    private bool CanFormat => _ready && _layout != EditorLayout.Preview;

    private string EditorPaneClass => PaneClass(
        "markdown-editor-pane",
        visibleWhenNarrow: _narrowTab == NarrowTab.Write,
        visibleWhenWide: _layout != EditorLayout.Preview);

    private string PreviewPaneClass => PaneClass(
        "markdown-preview-pane",
        visibleWhenNarrow: _narrowTab == NarrowTab.Preview,
        visibleWhenWide: _layout != EditorLayout.Editor);

    /// <summary>Sends any change still waiting for the debounce to .NET, so <see cref="Value"/> is current.</summary>
    public async Task FlushAsync()
    {
        if (_editor is not null && !_disposed)
        {
            await _editor.InvokeVoidAsync("flush");
        }
    }

    /// <summary>Moves the keyboard focus into the editor.</summary>
    public async Task FocusAsync()
    {
        if (_editor is not null && !_disposed)
        {
            await _editor.InvokeVoidAsync("focus");
        }
    }

    /// <summary>Inserts text at the cursor, replacing the selection.</summary>
    public async Task InsertTextAsync(string text)
    {
        if (_editor is not null && !_disposed)
        {
            await _editor.InvokeVoidAsync("insertText", text);
        }
    }

    /// <summary>
    /// Inserts text as a paragraph of its own at the cursor (for example an image from the media picker), with blank
    /// lines around it as needed, and reports the new content right away.
    /// </summary>
    public async Task InsertBlockAsync(string text)
    {
        if (_editor is not null && !_disposed)
        {
            await _editor.InvokeVoidAsync("insertBlock", text);
            await FlushAsync();
        }
    }

    /// <summary>Called by editor.js with the new text once typing pauses.</summary>
    [JSInvokable]
    public async Task OnContentChanged(string markdown)
    {
        _editorValue = markdown;
        await ValueChanged.InvokeAsync(markdown);
        StateHasChanged();
    }

    /// <summary>Called by editor.js when the editor loses focus.</summary>
    [JSInvokable]
    public Task OnEditorBlur()
    {
        return OnBlur.InvokeAsync();
    }

    /// <summary>Called by editor.js for <c>Ctrl/Cmd+S</c>.</summary>
    [JSInvokable]
    public Task OnSaveRequested()
    {
        return OnSave.InvokeAsync();
    }

    /// <summary>Called by editor.js for the image command when <see cref="OnInsertImage"/> has a handler.</summary>
    [JSInvokable]
    public Task OnImageRequested()
    {
        return OnInsertImage.InvokeAsync();
    }

    /// <summary>
    /// Detects a value set by the parent (initial load, reload, restoring a backup) as opposed to an echo of the
    /// author's own typing, and schedules it to be pushed into CodeMirror.
    /// </summary>
    protected override void OnParametersSet()
    {
        var value = Value ?? string.Empty;
        if (!_initialized)
        {
            _initialized = true;
            _lastValueParameter = value;
            _editorValue = value;
            return;
        }

        if (value == _lastValueParameter)
        {
            return;
        }

        _lastValueParameter = value;
        if (value != _editorValue)
        {
            _editorValue = value;
            _pushValueToEditor = true;
        }
    }

    /// <summary>Creates the CodeMirror instance once, then pushes values set by the parent.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed)
        {
            return;
        }

        if (firstRender)
        {
            await CreateEditorAsync();
            return;
        }

        if (_pushValueToEditor && _editor is not null)
        {
            _pushValueToEditor = false;
            await _editor.InvokeVoidAsync("setValue", _editorValue);
        }
    }

    /// <summary>Loads editor.js and mounts CodeMirror in the host element.</summary>
    private async Task CreateEditorAsync()
    {
        try
        {
            _selfReference = DotNetObjectReference.Create(this);
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/editor.js");
            _editor = await _module.InvokeAsync<IJSObjectReference>("createEditor", _host, _selfReference, new
            {
                value = _editorValue,
                placeholder = Placeholder,
                imageHandler = OnInsertImage.HasDelegate
            });
            await _editor.InvokeVoidAsync("setPreview", _previewPane);

            // Anything the parent set while the module was loading was already passed as the initial value.
            _pushValueToEditor = false;
            _ready = true;
            StateHasChanged();
        }
        catch (JSException ex)
        {
            // Leaves the read-only fallback visible instead of breaking the whole page.
            Logger.LogError(ex, "The Markdown editor failed to start.");
        }
    }

    private async Task RunCommandAsync(string command)
    {
        if (_editor is not null)
        {
            await _editor.InvokeVoidAsync("runCommand", command);
        }
    }

    private async Task OnPreviewRenderedAsync()
    {
        if (_editor is not null && !_disposed)
        {
            await _editor.InvokeVoidAsync("syncPreview");
        }
    }

    private async Task SelectLayout(EditorLayout layout)
    {
        _layout = layout;
        await OnPreviewRenderedAsync();
    }

    private async Task SelectNarrowTab(NarrowTab tab)
    {
        _narrowTab = tab;
        await OnPreviewRenderedAsync();
    }

    /// <summary>
    /// Bootstrap classes for a pane: full width on narrow screens (shown only on its tab) and half width in the
    /// split layout on wide screens.
    /// </summary>
    private string PaneClass(string name, bool visibleWhenNarrow, bool visibleWhenWide)
    {
        var width = _layout == EditorLayout.Split ? "col-lg-6 split" : "col-lg-12";
        var narrow = visibleWhenNarrow ? "d-block" : "d-none";
        var wide = visibleWhenWide ? "d-lg-block" : "d-lg-none";

        return $"{name} col-12 {width} {narrow} {wide}";
    }

    /// <summary>ARIA state attributes need the strings "true"/"false"; Blazor drops a <see langword="false"/> bool attribute.</summary>
    private static string AriaBool(bool value)
    {
        return value ? "true" : "false";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        try
        {
            if (_editor is not null)
            {
                await _editor.InvokeVoidAsync("dispose");
                await _editor.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The browser side is already gone; there is nothing left to release.
        }

        _selfReference?.Dispose();
    }

    /// <summary>A toolbar button: the editor.js command it runs, its icon and its tooltip.</summary>
    private sealed record ToolbarButton(string Command, string Icon, string Title);

    /// <summary>A layout button.</summary>
    private sealed record LayoutOption(EditorLayout Layout, string Icon, string Title);

    /// <summary>Which panes are shown on wide screens.</summary>
    private enum EditorLayout
    {
        Split,
        Editor,
        Preview
    }

    /// <summary>Which pane is shown on narrow screens.</summary>
    private enum NarrowTab
    {
        Write,
        Preview
    }
}
