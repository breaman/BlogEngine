using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Validation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Components;

/// <summary>
/// Tag chips with autocomplete (design 6.4, A4, T1.12). Suggestions come from <see cref="ITagService"/>
/// (<c>GET /api/admin/tags?search=</c>); Enter, Tab and comma commit the typed text, and Backspace in an empty
/// box removes the last chip.
/// </summary>
/// <remarks>
/// Commits follow <see cref="TagInputRules"/>: a name that matches an existing tag case-insensitively becomes
/// that tag with its original casing (typing <c>c#</c> adds <c>C#</c>), and a duplicate of a chip already
/// present is rejected. When the suggestions haven't loaded yet, the commit looks the name up first, so fast
/// typing can't create a differently cased twin. The server applies the same rules on save.
/// </remarks>
/// <example>
/// <code>
/// &lt;TagInput Id="post-tags" @bind-Tags="Post.Tags" @bind-Tags:after="OnEdited" /&gt;
/// </code>
/// </example>
public sealed partial class TagInput : ComponentBase, IAsyncDisposable
{
    [Inject] private ITagService TagService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<TagInput> Logger { get; set; } = default!;

    /// <summary>The tag names on the post.</summary>
    [Parameter] public List<string> Tags { get; set; } = [];

    /// <summary>Raised with a new list whenever a chip is added or removed.</summary>
    [Parameter] public EventCallback<List<string>> TagsChanged { get; set; }

    /// <summary>Id of the text box, for labels and tests.</summary>
    [Parameter] public string Id { get; set; } = "tags";

    /// <summary>Floating label of the text box.</summary>
    [Parameter] public string Label { get; set; } = "Add tags";

    /// <summary>Most chips allowed; defaults to the post rule.</summary>
    [Parameter] public int MaxTags { get; set; } = PostEditValidator.MaxTags;

    /// <summary>Disables adding and removing tags.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Pause after typing before suggestions are fetched.</summary>
    [Parameter] public int SearchDelayMilliseconds { get; set; } = 250;

    private ElementReference _input;
    private IJSObjectReference? _module;
    private IJSObjectReference? _keyHandler;
    private CancellationTokenSource? _searchCancellation;
    private string _text = string.Empty;
    private List<TagDto> _suggestions = [];
    private int _activeIndex = -1;
    private string? _error;

    private bool IsOpen => _suggestions.Count > 0 && !Disabled;

    private string ListId => $"{Id}-suggestions";

    private string HelpId => $"{Id}-help";

    private string? ActiveDescendant => IsOpen && _activeIndex >= 0 ? OptionId(_activeIndex) : null;

    private string OptionId(int index)
    {
        return $"{Id}-option-{index}";
    }

    /// <summary>Attaches the key handling from admin.js (only in the browser; never while prerendering).</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/admin.js");
            _keyHandler = await _module.InvokeAsync<IJSObjectReference>("attachTagInput", _input);
        }
        catch (JSException ex)
        {
            // Without it Enter could submit the form, but tags can still be added from the suggestions.
            Logger.LogWarning(ex, "Tag input keyboard handling failed to load.");
        }
    }

    /// <summary>Tracks the text, commits comma-separated pastes, and refreshes the suggestions.</summary>
    private async Task OnInputAsync(ChangeEventArgs e)
    {
        _text = e.Value?.ToString() ?? string.Empty;
        _error = null;

        // Typing a comma is cancelled in admin.js, so a comma here comes from pasting a list: add all but the last.
        if (_text.Contains(',', StringComparison.Ordinal))
        {
            var parts = _text.Split(',');
            _text = string.Empty;
            foreach (var part in parts[..^1].Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                await CommitAsync(part);
            }

            _text = parts[^1].TrimStart();
        }

        await SearchAsync(_text);
    }

    /// <summary>Fetches suggestions after a short pause, cancelling any search the author has typed past.</summary>
    private async Task SearchAsync(string text)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            CloseSuggestions();
            return;
        }

        var cancellation = _searchCancellation = new CancellationTokenSource();
        try
        {
            if (SearchDelayMilliseconds > 0)
            {
                await Task.Delay(SearchDelayMilliseconds, cancellation.Token);
            }

            var results = await TagService.SearchAsync(text, cancellation.Token);
            _suggestions = [.. results.Where(s => !Tags.Contains(s.Name, StringComparer.OrdinalIgnoreCase))];
            _activeIndex = -1;
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this one.
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Loading tag suggestions failed.");
            CloseSuggestions();
        }
    }

    /// <summary>Keyboard handling; admin.js has already cancelled the browser defaults for these keys.</summary>
    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "Enter" or ",":
                await CommitCurrentAsync();
                break;
            case "Tab" when !e.ShiftKey && !string.IsNullOrWhiteSpace(_text):
                await CommitCurrentAsync();
                break;
            case "Backspace" when _text.Length == 0 && Tags.Count > 0:
                await RemoveAsync(Tags[^1]);
                break;
            case "ArrowDown" when IsOpen:
                _activeIndex = (_activeIndex + 1) % _suggestions.Count;
                break;
            case "ArrowUp" when IsOpen:
                _activeIndex = _activeIndex <= 0 ? _suggestions.Count - 1 : _activeIndex - 1;
                break;
            case "Escape":
                CloseSuggestions();
                break;
        }
    }

    /// <summary>Leaving the box adds what was typed, so a tag isn't lost when the author moves on and saves.</summary>
    private async Task OnBlurAsync()
    {
        if (!string.IsNullOrWhiteSpace(_text))
        {
            await CommitAsync(_text);
        }

        CloseSuggestions();
    }

    /// <summary>Commits the highlighted suggestion, or else the typed text.</summary>
    private Task CommitCurrentAsync()
    {
        if (IsOpen && _activeIndex >= 0)
        {
            return CommitAsync(_suggestions[_activeIndex].Name);
        }

        return string.IsNullOrWhiteSpace(_text) ? Task.CompletedTask : CommitAsync(_text);
    }

    /// <summary>Adds a chip, reusing an existing tag's casing, or shows why it can't be added.</summary>
    private async Task CommitAsync(string typed)
    {
        var textAtStart = _text;
        var result = TagInputRules.Resolve(typed, Tags, _suggestions, MaxTags);
        if (result is { IsValid: true, MatchedExisting: false })
        {
            // The suggestions may be stale or not loaded yet; ask for this exact name before creating a new tag.
            try
            {
                var matches = await TagService.SearchAsync(result.Name);
                result = TagInputRules.Resolve(typed, Tags, matches, MaxTags);
            }
            catch (HttpRequestException ex)
            {
                // The server resolves casing on save anyway, so keep the typed name.
                Logger.LogWarning(ex, "Checking for an existing tag failed.");
            }
        }

        if (!result.IsValid)
        {
            _error = result.Error;
            return;
        }

        _error = null;
        CloseSuggestions();

        // Keep anything the author typed while the lookup above was running.
        if (_text == textAtStart)
        {
            _text = string.Empty;
        }

        // Updated here as well as through TagsChanged, so a second commit before the parent re-renders sees it.
        Tags = [.. Tags, result.Name!];
        await TagsChanged.InvokeAsync(Tags);
    }

    private async Task RemoveAsync(string tag)
    {
        _error = null;
        Tags = [.. Tags.Where(t => t != tag)];
        await TagsChanged.InvokeAsync(Tags);
    }

    private void CloseSuggestions()
    {
        _searchCancellation?.Cancel();
        _suggestions = [];
        _activeIndex = -1;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();

        try
        {
            if (_keyHandler is not null)
            {
                await _keyHandler.InvokeVoidAsync("dispose");
                await _keyHandler.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The browser side is already gone.
        }
    }
}
