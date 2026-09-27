using System.Globalization;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Components;

/// <summary>
/// The post editor's preview link card (design 10.2, A14, T4.4): creates a private link to the saved post, lists the
/// links that still work with a copy button, and revokes them.
/// </summary>
/// <remarks>
/// Links are loaded once the component runs in the browser (they aren't needed for the prerendered page), and again
/// when <see cref="PostId"/> changes, for example when a new post gets its id on its first save.
/// </remarks>
/// <example>
/// <code>
/// &lt;PreviewLinksCard PostId="Post.Id" /&gt;
/// </code>
/// </example>
public sealed partial class PreviewLinksCard : ComponentBase
{
    [Inject] private IPreviewLinkService PreviewLinks { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<PreviewLinksCard> Logger { get; set; } = default!;

    /// <summary>The post; 0 while it hasn't been saved yet.</summary>
    [Parameter, EditorRequired] public int PostId { get; set; }

    private List<PreviewLinkDto> _links = [];
    private int _loadedPostId;
    private bool _ready;
    private bool _busy;

    /// <summary>Loads the links for a new <see cref="PostId"/> once interactive.</summary>
    protected override async Task OnParametersSetAsync()
    {
        if (_ready && PostId != _loadedPostId)
        {
            await LoadAsync();
        }
    }

    /// <summary>Loads the links on the first interactive render.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _ready = true;
            await LoadAsync();
            StateHasChanged();
        }
    }

    private async Task LoadAsync()
    {
        _loadedPostId = PostId;
        _links = [];
        if (PostId == 0)
        {
            return;
        }

        try
        {
            _links = [.. await PreviewLinks.GetLinksAsync(PostId) ?? []];
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Loading the preview links of post {PostId} failed.", PostId);
        }
    }

    /// <summary>Creates a link with the default lifetime and copies it.</summary>
    private async Task CreateAsync()
    {
        _busy = true;
        try
        {
            if (await PreviewLinks.CreateAsync(PostId, new CreatePreviewLinkRequest()) is { } link)
            {
                _links.Insert(0, link);
                await CopyAsync(AbsoluteUrl(link));
            }
            else
            {
                Toasts.ShowWarning("This post no longer exists, so no preview link was created.");
            }
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The preview link couldn't be created. Check your connection and try again.");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Revokes a link; its URL stops working at once.</summary>
    private async Task RevokeAsync(PreviewLinkDto link)
    {
        _busy = true;
        try
        {
            await PreviewLinks.RevokeAsync(PostId, link.Id);
            _links.Remove(link);
            Toasts.ShowSuccess("The preview link no longer works.", "Link revoked");
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The preview link couldn't be revoked. Check your connection and try again.");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Copies a link to the clipboard; the link stays visible to copy by hand if the browser refuses.</summary>
    private async Task CopyAsync(string url)
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", url);
            Toasts.ShowSuccess("The preview link is on your clipboard.", "Link copied");
        }
        catch (JSException)
        {
            Toasts.ShowInfo("Copy the preview link from the box.");
        }
    }

    private string AbsoluteUrl(PreviewLinkDto link)
    {
        return Navigation.ToAbsoluteUri(link.Path).AbsoluteUri;
    }

    private static string ExpiresText(DateTimeOffset expiresOn)
    {
        var local = expiresOn.ToLocalTime();
        return string.Create(CultureInfo.CurrentCulture, $"{local:MMM d} at {local:t}");
    }
}