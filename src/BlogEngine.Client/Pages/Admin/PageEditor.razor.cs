using System.Globalization;

using BlogEngine.Client.Components;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Validation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The standalone page editor at <c>/admin/pages/new</c> and <c>/admin/pages/{id}</c> (design 6.7, 7.3, A17): title and
/// the same Markdown editor and preview as posts, with a sidebar for publishing, the slug, navigation visibility and
/// order, the summary and SEO overrides.
/// </summary>
/// <remarks>
/// <para>
/// Pages are deliberately simpler than posts (<see cref="PostEditor"/>): no autosave, staging, revisions or local
/// backups. <b>Save draft</b> and <b>Publish</b> save a draft; on a published page <b>Update</b> (or <c>Ctrl/Cmd+S</c>)
/// makes the changes live at once. Leaving with unsaved changes asks first, through <see cref="NavigationLock"/> for
/// programmatic navigation and closing the tab, and the admin.js link guard for link clicks.
/// </para>
/// <para>
/// <c>/admin/pages/new?template=privacy</c> starts from a template in <see cref="PageTemplates"/>; nothing is created
/// until the author saves.
/// </para>
/// </remarks>
public partial class PageEditor : ComponentBase, IAsyncDisposable
{
    [Inject] private IPageAdminService Pages { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<PageEditor> Logger { get; set; } = default!;

    /// <summary>The page id from the route; <see langword="null"/> on <c>/admin/pages/new</c>.</summary>
    [Parameter] public int? Id { get; set; }

    /// <summary>A template to start a new page from (<c>?template=privacy</c>).</summary>
    [SupplyParameterFromQuery(Name = "template")]
    public string? TemplateKey { get; set; }

    /// <summary>The page being edited; loaded while prerendering and restored in the browser.</summary>
    [PersistentState]
    public PageEditDto? Page { get; set; }

    /// <summary>Whether the requested page doesn't exist.</summary>
    [PersistentState]
    public bool PageMissing { get; set; }

    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private MarkdownEditor? _markdownEditor;
    private ConfirmDialog _confirm = default!;
    private MediaPicker _mediaPicker = default!;
    private EditContext? _editContext;
    private ValidationMessageStore? _serverErrors;

    /// <summary>The page as the server last returned it.</summary>
    private PageEditDto? _stored;

    /// <summary>Bumped on every edit; compared with <see cref="_savedVersion"/> to detect unsaved changes.</summary>
    private int _editVersion;

    /// <summary>The <see cref="_editVersion"/> captured by the last successful save.</summary>
    private int _savedVersion;

    /// <summary>The route id the current state belongs to; <see langword="null"/> before the first load.</summary>
    private int? _loadedRouteId;

    private bool _saving;
    private bool _interactive;
    private DateTimeOffset? _savedAt;
    private string? _formError;
    private string? _loadError;
    private DotNetObjectReference<PageEditor>? _selfReference;
    private IJSObjectReference? _adminModule;
    private IJSObjectReference? _linkGuard;
    private bool _linkGuardEnabled;
    private bool _leaveApproved;

    /// <summary>The highest navigation position, for the order input.</summary>
    private static int MaxNavOrder => PageEditValidator.MaxNavOrder;

    private bool IsNew => Page is { Id: 0 };

    private bool IsPublished => Page?.Status == PostStatus.Published;

    /// <summary>Buttons only work once WebAssembly runs; while prerendered they would do nothing.</summary>
    private bool CanAct => _interactive && !_saving && Page is not null;

    /// <summary>Edits made since the last save.</summary>
    private bool HasUnsavedChanges => Page is not null && _editVersion != _savedVersion;

    private string PageHeading => IsNew ? "New page" : "Edit page";

    /// <summary>Whether Update will move a published page to a new slug, which records a redirect.</summary>
    private bool SlugWillRedirect => IsPublished && _stored is not null && Page is not null
        && !string.IsNullOrWhiteSpace(Page.Slug) && Page.Slug != _stored.Slug;

    /// <summary>Loads the page for the route, or starts a new one, unless restored state already belongs to it.</summary>
    protected override async Task OnParametersSetAsync()
    {
        var routeId = Id ?? 0;
        if (_loadedRouteId == routeId)
        {
            return;
        }

        var firstLoad = _loadedRouteId is null;
        _loadedRouteId = routeId;

        if (firstLoad && (PageMissing || Page?.Id == routeId) && routeId != 0)
        {
            // Restored from the prerendered page; no need to fetch it again.
            if (Page is not null)
            {
                AttachModel(Page);
            }

            return;
        }

        await LoadAsync(routeId);
    }

    /// <summary>Installs the unsaved-changes link guard once the page runs in the browser.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _interactive = true;
            await AttachLinkGuardAsync();
            StateHasChanged();
        }

        await SyncLinkGuardAsync();
    }

    /// <summary>Loads a page from the service, or starts a blank or templated one for <c>/admin/pages/new</c>.</summary>
    private async Task LoadAsync(int id)
    {
        _loadError = null;
        try
        {
            Page = id == 0 ? NewPage() : await Pages.GetPageAsync(id);
            PageMissing = Page is null;
            if (Page is null)
            {
                return;
            }

            AttachModel(Page);
            if (id == 0 && PageTemplates.Find(TemplateKey) is not null)
            {
                // A template's content isn't saved yet, so leaving asks first.
                _editVersion++;
            }
        }
        catch (HttpRequestException)
        {
            _loadError = "The page couldn't be loaded. Check your connection and reload the page.";
        }
    }

    /// <summary>A blank page, or one filled in from the requested template.</summary>
    private PageEditDto NewPage()
    {
        return PageTemplates.Find(TemplateKey) is { } template
            ? new PageEditDto { Title = template.Title, Slug = template.Slug, Summary = template.Summary, ContentMarkdown = template.Markdown }
            : new PageEditDto();
    }

    /// <summary>Resets the editor state around a freshly loaded page.</summary>
    private void AttachModel(PageEditDto page)
    {
        _stored = page.Clone();
        _editVersion = 0;
        _savedVersion = 0;
        _savedAt = page.ModifiedOn;
        _formError = null;

        _editContext = new EditContext(page);
        _serverErrors = new ValidationMessageStore(_editContext);
        _editContext.OnFieldChanged += (_, e) => _serverErrors.Clear(e.FieldIdentifier);
    }

    // ---- Editing -------------------------------------------------------------------------------------------

    private void OnTitleEdited()
    {
        _editContext?.NotifyFieldChanged(FieldIdentifier.Create(() => Page!.Title));
        OnEdited();
    }

    private void OnMetaTitleEdited()
    {
        _editContext?.NotifyFieldChanged(FieldIdentifier.Create(() => Page!.MetaTitle));
        OnEdited();
    }

    private void OnMetaDescriptionEdited()
    {
        _editContext?.NotifyFieldChanged(FieldIdentifier.Create(() => Page!.MetaDescription));
        OnEdited();
    }

    /// <summary>Records an edit, so the page counts as having unsaved changes.</summary>
    private void OnEdited()
    {
        _editVersion++;
    }

    /// <summary>Opens the media picker and inserts the chosen image at the cursor (design 9.5).</summary>
    private async Task InsertImageAsync()
    {
        if (await _mediaPicker.ShowAsync() is { } markdown && _markdownEditor is not null)
        {
            await _markdownEditor.InsertBlockAsync(markdown);
        }
    }

    // ---- Saving --------------------------------------------------------------------------------------------

    private Task SaveDraftAsync()
    {
        return SaveAsync(publish: false);
    }

    private Task UpdateAsync()
    {
        return SaveAsync(publish: false);
    }

    private Task PublishAsync()
    {
        return SaveAsync(publish: true);
    }

    /// <summary><c>Ctrl/Cmd+S</c>: saves the page; a published page's changes go live, as with Update.</summary>
    private Task SaveShortcutAsync()
    {
        return SaveAsync(publish: false);
    }

    /// <summary>Saves the page (creating it the first time), then publishes it when asked to.</summary>
    private async Task SaveAsync(bool publish)
    {
        if (Page is null || _editContext is null)
        {
            return;
        }

        // Make sure the Markdown includes what was typed during the editor's debounce.
        if (_markdownEditor is not null)
        {
            await _markdownEditor.FlushAsync();
        }

        if (!_editContext.Validate())
        {
            Toasts.ShowError("Fix the highlighted fields before saving.");
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            _saving = true;
            _formError = null;

            var version = _editVersion;
            var sent = Page.Clone();
            var wasPublished = sent.Status == PostStatus.Published;
            var result = sent.Id == 0 ? await Pages.CreateAsync(sent) : await Pages.UpdateAsync(sent.Id, sent);
            if (publish && result is PageSaved created && created.Page.Status != PostStatus.Published)
            {
                result = await Pages.PublishAsync(created.Page.Id);
            }

            switch (result)
            {
                case PageSaved saved:
                    await OnSavedAsync(sent, saved.Page, version);
                    Toasts.ShowSuccess(publish ? "Your page is live." : wasPublished ? "Your changes are live." : "Draft saved.",
                        publish ? "Published" : wasPublished ? "Updated" : null);
                    break;
                case PageInvalid invalid:
                    ShowServerErrors(invalid.Errors);
                    Toasts.ShowError(_formError ?? "Fix the highlighted fields before saving.");
                    break;
                case PageNotFound:
                    _formError = "This page no longer exists. It may have been deleted in another tab.";
                    break;
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Saving page {PageId} failed.", Page.Id);
            Toasts.ShowError("The page couldn't be saved. Check your connection and try again.");
        }
        finally
        {
            _saving = false;
            _saveLock.Release();
        }
    }

    /// <summary>Copies the server's values back and marks the edits up to <paramref name="version"/> as saved.</summary>
    private async Task OnSavedAsync(PageEditDto sent, PageEditDto saved, int version)
    {
        var page = Page!;
        var wasNew = page.Id == 0;

        page.Id = saved.Id;
        page.Status = saved.Status;
        page.ModifiedOn = saved.ModifiedOn;
        page.PublicPath = saved.PublicPath;

        // The server may generate the slug and summary; keep them unless the author changed the field meanwhile.
        if (page.Slug == sent.Slug)
        {
            page.Slug = saved.Slug;
        }

        if (page.Summary == sent.Summary)
        {
            page.Summary = saved.Summary;
        }

        _stored = saved.Clone();
        _savedVersion = version;
        _savedAt = saved.ModifiedOn;

        if (wasNew)
        {
            // Replace the URL in place: navigating would re-render the page and reset the editor.
            try
            {
                await JS.InvokeVoidAsync("history.replaceState", null, string.Empty, Navigation.ToAbsoluteUri($"admin/pages/{page.Id}").ToString());
            }
            catch (JSException ex)
            {
                Logger.LogWarning(ex, "Updating the address bar for page {PageId} failed.", page.Id);
            }
        }
    }

    /// <summary>Returns the page to draft after confirming; its URL stops working and it leaves the navigation.</summary>
    private async Task UnpublishAsync()
    {
        if (Page is null)
        {
            return;
        }

        var confirmed = await _confirm.ConfirmAsync(
            "Unpublish page?",
            "The page goes back to being a draft: its URL stops working and it leaves the navigation until you publish it again.",
            "Unpublish",
            "btn-warning");
        if (!confirmed)
        {
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            _saving = true;
            if (await Pages.UnpublishAsync(Page.Id) is PageSaved saved)
            {
                Page.Status = saved.Page.Status;
                Page.PublicPath = saved.Page.PublicPath;
                _stored!.Status = saved.Page.Status;
                _stored.PublicPath = saved.Page.PublicPath;
                Toasts.ShowSuccess("The page is a draft again.", "Unpublished");
            }
            else
            {
                _formError = "This page no longer exists. It may have been deleted in another tab.";
            }
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The page couldn't be unpublished. Check your connection and try again.");
        }
        finally
        {
            _saving = false;
            _saveLock.Release();
        }
    }

    /// <summary>Shows validation errors returned by the server next to their fields; unknown keys become a form error.</summary>
    private void ShowServerErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        if (_editContext is null || _serverErrors is null)
        {
            return;
        }

        _serverErrors.Clear();
        var formErrors = new List<string>();
        foreach (var (key, messages) in errors)
        {
            if (key.Length > 0 && typeof(PageEditDto).GetProperty(key) is not null)
            {
                _serverErrors.Add(_editContext.Field(key), messages);
            }
            else
            {
                formErrors.AddRange(messages);
            }
        }

        _formError = formErrors.Count > 0 ? string.Join(" ", formErrors) : null;
        _editContext.NotifyValidationStateChanged();
    }

    // ---- Leaving -------------------------------------------------------------------------------------------

    /// <summary>Programmatic navigation: asks before leaving unsaved changes.</summary>
    private async Task OnBeforeInternalNavigationAsync(LocationChangingContext context)
    {
        if (_leaveApproved)
        {
            // Already confirmed through the link guard.
            _leaveApproved = false;
            return;
        }

        if (!await CanLeaveAsync())
        {
            context.PreventNavigation();
        }
    }

    /// <summary>
    /// Called by admin.js for an in-app link clicked while there are unsaved changes; link clicks use enhanced
    /// navigation, which never reaches <see cref="NavigationLock"/>.
    /// </summary>
    [JSInvokable]
    public async Task OnGuardedLinkClicked(string href)
    {
        if (await CanLeaveAsync())
        {
            _leaveApproved = true;
            Navigation.NavigateTo(href);
        }
    }

    private async Task<bool> CanLeaveAsync()
    {
        return !HasUnsavedChanges || await _confirm.ConfirmAsync(
            "Leave without saving?",
            "Some changes to this page haven't been saved and will be lost.",
            "Leave",
            "btn-danger");
    }

    /// <summary>Installs the admin.js link guard; it stays disabled until there is something to lose.</summary>
    private async Task AttachLinkGuardAsync()
    {
        try
        {
            _selfReference = DotNetObjectReference.Create(this);
            _adminModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/admin.js");
            _linkGuard = await _adminModule.InvokeAsync<IJSObjectReference>("guardLinkNavigation", _selfReference);
        }
        catch (JSException ex)
        {
            // The beforeunload prompt still protects the work.
            Logger.LogWarning(ex, "The unsaved-changes link guard failed to load.");
        }
    }

    /// <summary>Turns the link guard on while leaving would lose changes, and off otherwise.</summary>
    private async Task SyncLinkGuardAsync()
    {
        if (_linkGuard is null || _linkGuardEnabled == HasUnsavedChanges)
        {
            return;
        }

        _linkGuardEnabled = HasUnsavedChanges;
        await _linkGuard.InvokeVoidAsync("setEnabled", _linkGuardEnabled);
    }

    // ---- Display -------------------------------------------------------------------------------------------

    /// <summary>The save status line under the publish buttons.</summary>
    private (string Icon, string Text, string CssClass) StatusLine => (_saving, HasUnsavedChanges, IsNew) switch
    {
        (true, _, _) => ("bi-arrow-repeat", "Saving…", "text-body-secondary"),
        (_, true, _) => ("bi-pencil", "Unsaved changes", "text-body-secondary"),
        (_, _, true) => ("bi-file-earmark", "Not saved yet", "text-body-secondary"),
        _ => ("bi-check2-circle", $"Saved {_savedAt?.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)}", "text-success")
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_linkGuard is not null)
            {
                await _linkGuard.InvokeVoidAsync("dispose");
                await _linkGuard.DisposeAsync();
            }

            if (_adminModule is not null)
            {
                await _adminModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The browser side is already gone.
        }

        _selfReference?.Dispose();

        // _saveLock isn't disposed: a save still awaiting the server releases it when it completes.
        GC.SuppressFinalize(this);
    }
}
