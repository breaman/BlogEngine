using System.Globalization;

using BlogEngine.Client.Components;
using BlogEngine.Client.Services;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;

using FluentValidation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The post editor at <c>/admin/posts/new</c> and <c>/admin/posts/{id}</c> (design 10.2, T1.13): title, Markdown
/// editor with live preview, and a sidebar with publishing and scheduling, slug, summary, cover image, tags, options,
/// SEO overrides and reading stats.
/// </summary>
/// <remarks>
/// <para>
/// <b>Saving.</b> Changes are autosaved every 30 seconds and when the editor loses focus, but only while there
/// is something new to save. A new post is created by its first save. On a draft, autosave updates the post;
/// on a published post it only stages the title and content (Q3), and <b>Update</b> makes every change live.
/// <c>Ctrl/Cmd+S</c> saves a draft, or stages a published post's content like autosave, so it never makes
/// anything live by accident.
/// </para>
/// <para>
/// <b>Server-computed fields.</b> A save can change the slug (generated from the title, made unique), the
/// summary (generated from the content) and tag casing. They are copied back only if the author didn't
/// change that field while the save was in flight, so a slow save never overwrites fresh typing.
/// </para>
/// <para>
/// <b>Safety nets.</b> Every edit is backed up to <c>localStorage</c> (<see cref="DraftBackupStore"/>) and the
/// editor offers to restore a backup that differs from the stored post. Leaving the page with unsaved changes
/// asks first: <see cref="NavigationLock"/> covers programmatic navigation and closing or reloading the tab
/// (<c>beforeunload</c>), and a link guard in admin.js covers link clicks, which enhanced navigation would
/// otherwise follow without asking. A
/// stale <see cref="PostEditDto.RowVersion"/> (the post changed in another tab) asks whether to reload or overwrite.
/// </para>
/// </remarks>
public partial class PostEditor : ComponentBase, IAsyncDisposable
{
    /// <summary>Value formats of a <c>datetime-local</c> input, with and without seconds.</summary>
    private static readonly string[] ScheduleInputFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFF"];

    /// <summary>How often autosave runs while there are unsaved changes (design 10.2).</summary>
    private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(30);

    [Inject] private IPostAdminService Posts { get; set; } = default!;
    [Inject] private ISettingsService Settings { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;
    [Inject] private IValidator<PostEditDto> Validator { get; set; } = default!;
    [Inject] private DraftBackupStore Backups { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<PostEditor> Logger { get; set; } = default!;

    /// <summary>The post id from the route; <see langword="null"/> on <c>/admin/posts/new</c>.</summary>
    [Parameter] public int? Id { get; set; }

    /// <summary>
    /// A revision to load into the editor as unsaved changes (<c>?restore=12</c>), set by the revision history page's
    /// Restore button (A13, T4.3).
    /// </summary>
    [SupplyParameterFromQuery(Name = "restore")]
    public int? RestoreRevisionId { get; set; }

    /// <summary>The post being edited; loaded while prerendering and restored in the browser.</summary>
    [PersistentState]
    public PostEditDto? Post { get; set; }

    /// <summary>Whether the requested post doesn't exist (or is in the trash).</summary>
    [PersistentState]
    public bool PostMissing { get; set; }

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    private MarkdownEditor? _markdownEditor;
    private ConfirmDialog _confirm = default!;
    private MediaPicker _mediaPicker = default!;
    private EditContext? _editContext;
    private ValidationMessageStore? _serverErrors;

    /// <summary>The post as the server last returned it; for a published post, what is live.</summary>
    private PostEditDto? _stored;

    /// <summary>Bumped on every edit; compared with <see cref="_savedVersion"/> to detect unsaved changes.</summary>
    private int _editVersion;

    /// <summary>The <see cref="_editVersion"/> captured by the last successful save.</summary>
    private int _savedVersion;

    /// <summary>The route id the current state belongs to; <see langword="null"/> before the first load.</summary>
    private int? _loadedRouteId;

    private SaveStatus _status = SaveStatus.Idle;
    private DateTimeOffset? _savedAt;
    private string? _formError;
    private string? _autosaveProblem;
    private string? _loadError;
    private bool _interactive;
    private bool _checkBackup;
    private bool _conflictUnresolved;
    private PostDraftBackup? _backupOffer;
    private bool _showPendingChanges;
    private SlugCheckResult? _slugCheck;
    private CancellationTokenSource? _slugCheckCancellation;
    private ReadingStats _stats;
    private PeriodicTimer? _autosaveTimer;
    private DotNetObjectReference<PostEditor>? _selfReference;
    private IJSObjectReference? _adminModule;
    private IJSObjectReference? _linkGuard;
    private bool _linkGuardEnabled;
    private bool _leaveApproved;

    /// <summary>The blog's time zone, in which the schedule picker works (design 10.2, Q10); loaded once.</summary>
    private string? _timeZoneId;

    /// <summary>Whether the schedule picker is open.</summary>
    private bool _showSchedule;

    /// <summary>
    /// The schedule picker's value as the <c>datetime-local</c> input reports it (<c>2026-10-01T09:00</c>): a wall-clock
    /// time in <see cref="_timeZoneId"/>. Kept as text and parsed on submit, because browsers differ on whether they
    /// include seconds.
    /// </summary>
    private string? _scheduleText;

    /// <summary>Why the schedule picker's value can't be used, shown under it.</summary>
    private string? _scheduleError;

    /// <summary>The publish time sent by a <see cref="SaveKind.Schedule"/> save.</summary>
    private DateTimeOffset? _scheduleOn;

    /// <summary>The <see cref="RestoreRevisionId"/> already applied, so a re-render doesn't apply it twice.</summary>
    private int? _restoredRevisionId;

    private bool IsNew => Post is { Id: 0 };

    /// <summary>Published or scheduled: edits are staged, and <b>Update</b> saves them (Q3).</summary>
    private bool IsPublished => Post?.Status == PostStatus.Published;

    /// <summary>Published with a date that is still ahead (design 6.3, A9).</summary>
    private bool IsScheduled => Post is not null && PostSchedule.IsScheduled(Post.Status, Post.PublishedOn, TimeProvider.GetUtcNow());

    /// <summary>Published and past its publish time, so readers can see it.</summary>
    private bool IsLive => Post is not null && PostSchedule.IsLive(Post.Status, Post.PublishedOn, TimeProvider.GetUtcNow());

    private bool IsBusy => _status == SaveStatus.Saving;

    /// <summary>Buttons only work once WebAssembly runs; while prerendered they would do nothing.</summary>
    private bool CanAct => _interactive && !IsBusy && Post is not null;

    /// <summary>Edits made since the last save.</summary>
    private bool HasUnsavedEdits => _editVersion != _savedVersion;

    /// <summary>
    /// Changes that would be lost by leaving: unsaved edits, plus, on a published post, slug, summary, tag or
    /// option changes, which autosave doesn't stage and only <b>Update</b> saves.
    /// </summary>
    private bool HasUnsavedChanges => Post is not null
        && (HasUnsavedEdits || (IsPublished && _stored is not null && !PostEdits.DetailsEqual(Post, _stored)));

    /// <summary>On a published post, whether the editor differs from what is live.</summary>
    private bool HasChangesNotLive => IsPublished && Post is not null && _stored is not null && !PostEdits.AreEqual(Post, _stored);

    private string PageHeading => IsNew ? "New post" : "Edit post";

    /// <summary>Loads the post for the route, or starts a new one, unless restored state already belongs to it.</summary>
    protected override async Task OnParametersSetAsync()
    {
        await LoadTimeZoneAsync();

        var routeId = Id ?? 0;
        if (_loadedRouteId == routeId)
        {
            return;
        }

        var firstLoad = _loadedRouteId is null;
        _loadedRouteId = routeId;

        if (firstLoad && (PostMissing || Post?.Id == routeId))
        {
            // Restored from the prerendered page; no need to fetch it again.
            if (Post is not null)
            {
                AttachModel(Post);
            }

            return;
        }

        await LoadAsync(routeId);
    }

    /// <summary>Starts autosave and checks for a local backup once the page runs in the browser.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _interactive = true;
            _ = RunAutosaveLoopAsync();
            await AttachLinkGuardAsync();
            StateHasChanged();
        }

        await SyncLinkGuardAsync();

        if (_checkBackup && Post is not null)
        {
            _checkBackup = false;
            await OfferBackupAsync();
        }

        // After the backup check, so the restored revision (now unsaved) isn't mistaken for a stale backup.
        if (_interactive && Post is { Id: > 0 } && RestoreRevisionId is { } revisionId && _restoredRevisionId != revisionId)
        {
            _restoredRevisionId = revisionId;
            await RestoreRevisionAsync(revisionId);
        }
    }

    /// <summary>Loads the blog's time zone for the schedule picker, once.</summary>
    private async Task LoadTimeZoneAsync()
    {
        if (_timeZoneId is not null)
        {
            return;
        }

        try
        {
            _timeZoneId = (await Settings.GetAsync()).TimeZoneId;
        }
        catch (HttpRequestException ex)
        {
            // Only scheduling needs it; the picker says so if it is still missing.
            Logger.LogWarning(ex, "Loading the blog's time zone failed.");
        }
    }

    /// <summary>Loads a post from the service, or starts a blank one for <c>/admin/posts/new</c>.</summary>
    private async Task LoadAsync(int id)
    {
        _loadError = null;
        try
        {
            Post = id == 0 ? new PostEditDto() : await Posts.GetPostAsync(id);
            PostMissing = Post is null;
            if (Post is not null)
            {
                AttachModel(Post);
            }
        }
        catch (HttpRequestException)
        {
            _loadError = "The post couldn't be loaded. Check your connection and reload the page.";
        }
    }

    /// <summary>Resets the editor state around a freshly loaded post.</summary>
    private void AttachModel(PostEditDto post)
    {
        _stored = post.Clone();
        _editVersion = 0;
        _savedVersion = 0;
        _status = SaveStatus.Idle;
        _savedAt = post.ModifiedOn;
        _formError = null;
        _slugCheck = null;
        _backupOffer = null;
        _conflictUnresolved = false;
        _showSchedule = false;
        _scheduleError = null;
        _showPendingChanges = post.PendingChanges is not null;
        _stats = ReadingTime.Calculate(post.ContentMarkdown);

        _editContext = new EditContext(post);
        _serverErrors = new ValidationMessageStore(_editContext);
        _editContext.OnFieldChanged += (_, e) => _serverErrors.Clear(e.FieldIdentifier);

        // Checked after the next render, because localStorage only exists in the browser.
        _checkBackup = true;
    }

    // ---- Editing -------------------------------------------------------------------------------------------

    private Task OnTitleEditedAsync()
    {
        _editContext?.NotifyFieldChanged(FieldIdentifier.Create(() => Post!.Title));
        return OnEditedAsync();
    }

    private Task OnContentEditedAsync()
    {
        _stats = ReadingTime.Calculate(Post?.ContentMarkdown);
        return OnEditedAsync();
    }

    private Task OnTagsEditedAsync()
    {
        _editContext?.NotifyFieldChanged(FieldIdentifier.Create(() => Post!.Tags));
        return OnEditedAsync();
    }

    private Task OnMetaTitleEditedAsync()
    {
        _editContext?.NotifyFieldChanged(FieldIdentifier.Create(() => Post!.MetaTitle));
        return OnEditedAsync();
    }

    private Task OnMetaDescriptionEditedAsync()
    {
        _editContext?.NotifyFieldChanged(FieldIdentifier.Create(() => Post!.MetaDescription));
        return OnEditedAsync();
    }

    /// <summary>Picks the cover image (A15) from the media library.</summary>
    private async Task ChooseCoverAsync()
    {
        if (Post is not null && await _mediaPicker.PickAsync("Choose a cover image") is { } item)
        {
            Post.CoverMediaId = item.Id;
            Post.CoverImage = PostImageDto.From(item);
            await OnImageEditedAsync(nameof(PostEditDto.CoverMediaId));
        }
    }

    private Task RemoveCoverAsync()
    {
        Post!.CoverMediaId = null;
        Post.CoverImage = null;
        return OnImageEditedAsync(nameof(PostEditDto.CoverMediaId));
    }

    /// <summary>Picks the social sharing image (A16) from the media library.</summary>
    private async Task ChooseSocialImageAsync()
    {
        if (Post is not null && await _mediaPicker.PickAsync("Choose a social image") is { } item)
        {
            Post.SocialImageMediaId = item.Id;
            Post.SocialImage = PostImageDto.From(item);
            await OnImageEditedAsync(nameof(PostEditDto.SocialImageMediaId));
        }
    }

    private Task RemoveSocialImageAsync()
    {
        Post!.SocialImageMediaId = null;
        Post.SocialImage = null;
        return OnImageEditedAsync(nameof(PostEditDto.SocialImageMediaId));
    }

    /// <summary>Records an image change, clearing a server error on its field (such as "no longer in the library").</summary>
    private Task OnImageEditedAsync(string fieldName)
    {
        _editContext?.NotifyFieldChanged(_editContext.Field(fieldName));
        return OnEditedAsync();
    }

    private async Task OnSlugEditedAsync()
    {
        await OnEditedAsync();
        await CheckSlugAsync();
    }

    /// <summary>Records an edit and backs it up locally.</summary>
    private async Task OnEditedAsync()
    {
        _editVersion++;
        if (_status is SaveStatus.Saved or SaveStatus.Idle)
        {
            _status = SaveStatus.Idle;
        }

        await BackupAsync();
    }

    /// <summary>Writes the local backup while there are unsaved changes, and removes it once there are none.</summary>
    private async Task BackupAsync()
    {
        if (!_interactive || Post is null)
        {
            return;
        }

        if (HasUnsavedChanges)
        {
            await Backups.SaveAsync(Post.Id, PostDraftBackup.From(Post, TimeProvider.GetUtcNow()));
        }
        else
        {
            await Backups.RemoveAsync(Post.Id);
        }
    }

    /// <summary>Checks the slug inline: format, availability, and the slug a save would actually use.</summary>
    private async Task CheckSlugAsync()
    {
        if (Post is null)
        {
            return;
        }

        _slugCheckCancellation?.Cancel();
        _slugCheckCancellation?.Dispose();
        var cancellation = _slugCheckCancellation = new CancellationTokenSource();

        try
        {
            _slugCheck = await Posts.CheckSlugAsync(new SlugCheckRequest
            {
                Slug = Post.Slug,
                Title = Post.Title,
                PostId = IsNew ? null : Post.Id
            }, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer check replaced this one.
        }
        catch (HttpRequestException)
        {
            _slugCheck = null;
        }
    }

    // ---- Saving --------------------------------------------------------------------------------------------

    /// <summary>Runs autosave on a timer for as long as the page is open.</summary>
    private async Task RunAutosaveLoopAsync()
    {
        _autosaveTimer = new PeriodicTimer(AutosaveInterval);
        try
        {
            while (await _autosaveTimer.WaitForNextTickAsync(_lifetime.Token))
            {
                await InvokeAsync(async () =>
                {
                    await AutosaveAsync();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The page was closed.
        }
    }

    /// <summary>Autosaves when there is something new, the content is valid and no conflict is waiting on the author.</summary>
    private async Task AutosaveAsync()
    {
        if (!_interactive || Post is null || !HasUnsavedEdits || IsBusy || _conflictUnresolved)
        {
            return;
        }

        // Invalid content can't be saved (a new post needs a title). Say why in the status line rather than
        // highlighting fields the author hasn't finished with yet.
        var validation = await Validator.ValidateAsync(Post);
        if (!validation.IsValid)
        {
            _status = SaveStatus.Invalid;
            _autosaveProblem = validation.Errors[0].ErrorMessage;
            return;
        }

        _autosaveProblem = null;

        await SaveAsync(SaveKind.Autosave, showErrors: false);
    }

    /// <summary>
    /// <c>Ctrl/Cmd+S</c>: saves a draft, or stages a published post's content like autosave does. It never makes
    /// anything live; that takes <b>Update</b>.
    /// </summary>
    private async Task SaveShortcutAsync()
    {
        if (!IsPublished)
        {
            await SaveAsync(SaveKind.Draft);
            return;
        }

        await SaveAsync(SaveKind.Autosave);
        if (_status == SaveStatus.Saved)
        {
            Toasts.ShowInfo("Changes saved. They go live when you click Update.");
        }
    }

    /// <summary>
    /// The editor's image button and <c>Ctrl/Cmd+Shift+I</c>: opens the media picker and inserts the chosen image as a
    /// paragraph of its own at the cursor (design 9.5, T2.11).
    /// </summary>
    private async Task InsertImageAsync()
    {
        if (await _mediaPicker.ShowAsync() is { } markdown && _markdownEditor is not null)
        {
            await _markdownEditor.InsertBlockAsync(markdown);
        }
    }

    private Task SaveDraftAsync()
    {
        return SaveAsync(SaveKind.Draft);
    }

    private Task UpdateAsync()
    {
        return SaveAsync(SaveKind.Update);
    }

    private Task PublishAsync()
    {
        return SaveAsync(SaveKind.Publish);
    }

    /// <summary>
    /// Opens the schedule picker at the current scheduled time, or at the start of the next hour, in the blog's time
    /// zone.
    /// </summary>
    private void OpenSchedule()
    {
        _scheduleError = null;
        if (_timeZoneId is null)
        {
            _scheduleError = "The blog's time zone couldn't be loaded, so posts can't be scheduled right now. Reload the page and try again.";
            _showSchedule = true;
            return;
        }

        try
        {
            var start = IsScheduled
                ? Post!.PublishedOn!.Value
                : TimeProvider.GetUtcNow().AddHours(1);
            var local = BlogTimeZone.ToLocalDateTime(start, _timeZoneId);
            var initial = IsScheduled ? local : local.Date.AddHours(local.Hour);
            _scheduleText = initial.ToString(ScheduleInputFormats[0], CultureInfo.InvariantCulture);
            _showSchedule = true;
        }
        catch (TimeZoneNotFoundException)
        {
            _scheduleError = $"The time zone '{_timeZoneId}' isn't available in this browser, so posts can't be scheduled here.";
            _showSchedule = true;
        }
    }

    /// <summary>
    /// Keeps the picker's raw value. Not <c>@bind</c>: Razor binds <c>datetime-local</c> inputs to typed values with a
    /// fixed format, which rejected the browser's value and reverted the picker.
    /// </summary>
    private void OnScheduleChanged(ChangeEventArgs e)
    {
        _scheduleText = e.Value?.ToString();
        _scheduleError = null;
    }

    private void CancelSchedule()
    {
        _showSchedule = false;
        _scheduleError = null;
    }

    /// <summary>
    /// Saves the editor and publishes the post at the picked time (design 6.3, A9). The time is read in the blog's time
    /// zone, whatever the browser's own zone is, and must be in the future; to publish now there is <b>Publish now</b>.
    /// </summary>
    private async Task ScheduleAsync()
    {
        _scheduleError = null;
        if (_timeZoneId is null
            || !DateTime.TryParseExact(_scheduleText, ScheduleInputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            _scheduleError = "Choose the date and time to publish at.";
            return;
        }

        DateTimeOffset publishOn;
        try
        {
            publishOn = BlogTimeZone.FromLocalDateTime(local, _timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _scheduleError = $"The time zone '{_timeZoneId}' isn't available in this browser, so posts can't be scheduled here.";
            return;
        }

        if (publishOn <= TimeProvider.GetUtcNow())
        {
            _scheduleError = "Choose a time in the future, or use Publish now.";
            return;
        }

        _scheduleOn = publishOn;
        await SaveAsync(SaveKind.Schedule);
        if (_status == SaveStatus.Saved)
        {
            _showSchedule = false;
        }
    }

    /// <summary>
    /// Saves the editor through the right service call for <paramref name="kind"/>, then copies the server's
    /// computed values back. A conflict is resolved with the author after the save lock is released.
    /// </summary>
    private async Task SaveAsync(SaveKind kind, bool showErrors = true)
    {
        if (Post is null || _editContext is null)
        {
            return;
        }

        // Make sure the Markdown includes what was typed during the editor's debounce.
        if (_markdownEditor is not null)
        {
            await _markdownEditor.FlushAsync();
        }

        if (kind != SaveKind.Autosave && !_editContext.Validate())
        {
            Toasts.ShowError("Fix the highlighted fields before saving.");
            return;
        }

        PostSaveResult? result = null;
        await _saveLock.WaitAsync();
        try
        {
            _status = SaveStatus.Saving;
            _formError = null;
            StateHasChanged();

            var version = _editVersion;
            var sent = Post.Clone();
            result = await SendAsync(kind, sent);

            if (result is PostSaved saved)
            {
                await OnSavedAsync(kind, sent, saved.Post, version);
            }
            else
            {
                OnSaveFailed(result, showErrors);
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Saving post {PostId} failed.", Post.Id);
            _status = SaveStatus.Failed;
            if (showErrors)
            {
                Toasts.ShowError("The post couldn't be saved. Check your connection; your changes are kept in this browser.");
            }
        }
        finally
        {
            _saveLock.Release();
        }

        if (result is PostConflict)
        {
            await ResolveConflictAsync(kind);
        }
    }

    /// <summary>Calls the service operation(s) for a save.</summary>
    private async Task<PostSaveResult> SendAsync(SaveKind kind, PostEditDto sent)
    {
        if (sent.Id == 0)
        {
            var created = await Posts.CreateAsync(sent);
            if (kind is not (SaveKind.Publish or SaveKind.Schedule) || created is not PostSaved createdPost)
            {
                return created;
            }

            return await Posts.PublishAsync(createdPost.Post.Id, PublishRequest(kind, createdPost.Post.RowVersion));
        }

        return kind switch
        {
            SaveKind.Autosave => await Posts.AutosaveAsync(sent.Id, sent),
            SaveKind.Draft or SaveKind.Update => await Posts.UpdateAsync(sent.Id, sent),
            SaveKind.Publish or SaveKind.Schedule => await PublishExistingAsync(kind, sent),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    /// <summary>Saves the latest edits, then publishes the post now or at the scheduled time.</summary>
    private async Task<PostSaveResult> PublishExistingAsync(SaveKind kind, PostEditDto sent)
    {
        var updated = await Posts.UpdateAsync(sent.Id, sent);
        if (updated is not PostSaved saved)
        {
            return updated;
        }

        return await Posts.PublishAsync(sent.Id, PublishRequest(kind, saved.Post.RowVersion));
    }

    /// <summary>
    /// The publish request for a save: <see cref="SaveKind.Schedule"/> sends the picked time; <see cref="SaveKind.Publish"/>
    /// sends none, which publishes now (or keeps the original date of a post that was live before).
    /// </summary>
    private PublishPostRequest PublishRequest(SaveKind kind, byte[]? rowVersion)
    {
        return new PublishPostRequest
        {
            PublishOn = kind == SaveKind.Schedule ? _scheduleOn : null,
            RowVersion = rowVersion
        };
    }

    /// <summary>Applies a successful save to the editor state.</summary>
    private async Task OnSavedAsync(SaveKind kind, PostEditDto sent, PostEditDto saved, int version)
    {
        var post = Post!;
        var wasNew = post.Id == 0;

        // An autosave of a published post only staged the content; the server returns the live post, whose
        // editable fields must not replace what the author is editing.
        var stagedOnly = kind == SaveKind.Autosave && saved.Status == PostStatus.Published;
        ApplyServerValues(post, sent, saved, adoptEditableFields: !stagedOnly);

        if (stagedOnly)
        {
            _stored!.RowVersion = saved.RowVersion;
            _stored.PendingChanges = saved.PendingChanges;
        }
        else
        {
            _stored = saved.Clone();
        }

        _savedVersion = version;
        _savedAt = TimeProvider.GetUtcNow();
        _status = SaveStatus.Saved;

        if (wasNew)
        {
            await OnCreatedAsync(post.Id);
        }

        await BackupAsync();
        ShowSavedToast(kind);
    }

    /// <summary>Moves a new post's backup to its real key and puts its id in the address bar.</summary>
    private async Task OnCreatedAsync(int postId)
    {
        await Backups.RemoveAsync(0);

        // Replace the URL in place: navigating would re-render the page and reset the editor.
        try
        {
            await JS.InvokeVoidAsync("history.replaceState", null, string.Empty, Navigation.ToAbsoluteUri($"admin/posts/{postId}").ToString());
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "Updating the address bar for post {PostId} failed.", postId);
        }
    }

    /// <summary>Confirms explicit saves; autosave stays silent and only updates the status line.</summary>
    private void ShowSavedToast(SaveKind kind)
    {
        switch (kind)
        {
            case SaveKind.Publish:
                Toasts.ShowSuccess("Your post is live.", "Published");
                break;
            case SaveKind.Schedule:
                Toasts.ShowSuccess($"Your post will be published on {ScheduledText}.", "Scheduled");
                break;
            case SaveKind.Update:
                Toasts.ShowSuccess("Your changes are live.", "Updated");
                break;
            case SaveKind.Draft:
                Toasts.ShowSuccess("Draft saved.");
                break;
        }
    }

    /// <summary>Reports a failed save: validation errors next to their fields, the rest as a message.</summary>
    private void OnSaveFailed(PostSaveResult result, bool showErrors)
    {
        switch (result)
        {
            case PostInvalid invalid:
                _status = SaveStatus.Invalid;
                _autosaveProblem = null;
                ShowServerErrors(invalid.Errors);
                if (showErrors)
                {
                    Toasts.ShowError(_formError ?? "Fix the highlighted fields before saving.");
                }

                break;
            case PostNotFound:
                _status = SaveStatus.Failed;
                _formError = "This post no longer exists. It may have been moved to the trash in another tab.";
                break;
            case PostConflict:
                _status = SaveStatus.Conflict;
                break;
        }
    }

    /// <summary>
    /// The post changed elsewhere (another tab or session). The author chooses: overwrite it with this version,
    /// reload the stored version (this version stays in the local backup), or decide later.
    /// </summary>
    private async Task ResolveConflictAsync(SaveKind kind)
    {
        _conflictUnresolved = true;
        await BackupAsync();

        var choice = await _confirm.ShowAsync(new ConfirmOptions(
            "Changed in another tab",
            "This post was changed in another tab or by another session. Reload it, or overwrite it with your version? " +
            "If you reload, your version stays in this browser and you can restore it.")
        {
            ConfirmText = "Overwrite",
            ConfirmButtonClass = "btn-danger",
            AlternateText = "Reload",
            CancelText = "Decide later"
        });

        switch (choice)
        {
            case DialogChoice.Confirm:
                await OverwriteAsync(kind);
                break;
            case DialogChoice.Alternate:
                await ReloadAsync();
                break;
            default:
                _status = SaveStatus.Conflict;
                break;
        }
    }

    /// <summary>Takes the stored post's concurrency token and saves again, replacing the other change.</summary>
    private async Task OverwriteAsync(SaveKind kind)
    {
        var latest = Post is null ? null : await Posts.GetPostAsync(Post.Id);
        if (latest is null)
        {
            _formError = "This post no longer exists. It may have been moved to the trash in another tab.";
            _status = SaveStatus.Failed;
            return;
        }

        _conflictUnresolved = false;
        Post!.RowVersion = latest.RowVersion;
        await SaveAsync(kind);
    }

    /// <summary>Replaces the editor with the stored post; the local backup then offers the discarded version.</summary>
    private async Task ReloadAsync()
    {
        if (Post is null)
        {
            return;
        }

        await LoadAsync(Post.Id);
    }

    /// <summary>
    /// Returns the post to draft after confirming; unsaved edits stay in the editor and are saved as the draft. For a
    /// scheduled post this is <b>Unschedule</b>, which also forgets the scheduled date.
    /// </summary>
    private async Task UnpublishAsync()
    {
        var scheduled = IsScheduled;
        if (Post is null)
        {
            return;
        }

        var confirmed = scheduled
            ? await _confirm.ConfirmAsync(
                "Unschedule post?",
                "The post goes back to being a draft and won't be published at the scheduled time.",
                "Unschedule",
                "btn-warning")
            : await _confirm.ConfirmAsync(
                "Unpublish post?",
                "The post goes back to being a draft and its public URL stops working until you publish it again.",
                "Unpublish",
                "btn-warning");
        if (!confirmed)
        {
            return;
        }

        await _saveLock.WaitAsync();
        PostSaveResult? result = null;
        try
        {
            _status = SaveStatus.Saving;
            result = await Posts.UnpublishAsync(Post.Id, new UnpublishPostRequest { RowVersion = Post.RowVersion });
            if (result is PostSaved saved)
            {
                ApplyServerValues(Post, Post.Clone(), saved.Post, adoptEditableFields: false);
                _stored = saved.Post.Clone();
                _status = SaveStatus.Saved;

                // Edits that were staged or pending on the published post now belong in the draft.
                if (!PostEdits.AreEqual(Post, _stored))
                {
                    _editVersion++;
                }

                Toasts.ShowSuccess("The post is a draft again.", scheduled ? "Unscheduled" : "Unpublished");
            }
            else
            {
                OnSaveFailed(result, showErrors: true);
            }
        }
        catch (HttpRequestException)
        {
            _status = SaveStatus.Failed;
            Toasts.ShowError($"The post couldn't be {(scheduled ? "unscheduled" : "unpublished")}. Check your connection and try again.");
        }
        finally
        {
            _saveLock.Release();
        }

        if (result is PostConflict)
        {
            Toasts.ShowWarning($"The post was changed in another tab. Reload the page before {(scheduled ? "unscheduling" : "unpublishing")}.");
        }
    }

    /// <summary>
    /// Copies server-owned values from a save result. Editable fields the server may normalize (slug, summary,
    /// tags) are copied only when the author hasn't changed them since the request was sent.
    /// </summary>
    private static void ApplyServerValues(PostEditDto post, PostEditDto sent, PostEditDto saved, bool adoptEditableFields)
    {
        post.Id = saved.Id;
        post.RowVersion = saved.RowVersion;
        post.Status = saved.Status;
        post.PublishedOn = saved.PublishedOn;
        post.PublishedDateLocal = saved.PublishedDateLocal;
        post.LastUpdatedOn = saved.LastUpdatedOn;
        post.ModifiedOn = saved.ModifiedOn;
        post.WordCount = saved.WordCount;
        post.ReadingMinutes = saved.ReadingMinutes;
        post.PublicPath = saved.PublicPath;
        post.PendingChanges = saved.PendingChanges;

        // The server's thumbnails carry the images' current versions; keep them while the choice is unchanged.
        if (post.CoverMediaId == saved.CoverMediaId)
        {
            post.CoverImage = saved.CoverImage;
        }

        if (post.SocialImageMediaId == saved.SocialImageMediaId)
        {
            post.SocialImage = saved.SocialImage;
        }

        if (!adoptEditableFields)
        {
            return;
        }

        if (post.Slug == sent.Slug)
        {
            post.Slug = saved.Slug;
        }

        if (post.Summary == sent.Summary)
        {
            post.Summary = saved.Summary;
        }

        if (post.Tags.SequenceEqual(sent.Tags, StringComparer.Ordinal))
        {
            post.Tags = [.. saved.Tags];
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
            if (key.Length > 0 && typeof(PostEditDto).GetProperty(key) is not null)
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

    // ---- Recovery ------------------------------------------------------------------------------------------

    /// <summary>Offers a local backup that differs from the loaded post, and drops one that doesn't.</summary>
    private async Task OfferBackupAsync()
    {
        var post = Post!;
        var backup = await Backups.LoadAsync(post.Id);
        if (backup is null)
        {
            return;
        }

        // A backup older than the stored post was superseded, for example by a save in another tab.
        var stale = post.ModifiedOn is { } modified && backup.SavedAt < modified;
        if (stale || !backup.DiffersFrom(post))
        {
            await Backups.RemoveAsync(post.Id);
            return;
        }

        _backupOffer = backup;
        StateHasChanged();
    }

    private async Task RestoreBackupAsync()
    {
        if (Post is null || _backupOffer is null)
        {
            return;
        }

        _backupOffer.ApplyTo(Post);
        _backupOffer = null;
        _showPendingChanges = false;
        await OnContentEditedAsync();
        _editContext?.NotifyValidationStateChanged();
    }

    /// <summary>
    /// Loads a revision's title and content into the editor as unsaved changes (A13, T4.3). Nothing is saved until the
    /// author saves, or clicks Update on a published post; until then the local backup keeps the restored text.
    /// </summary>
    private async Task RestoreRevisionAsync(int revisionId)
    {
        var post = Post!;
        try
        {
            if (await Posts.GetRevisionAsync(post.Id, revisionId) is not { } revision)
            {
                Toasts.ShowWarning("That revision no longer exists, so nothing was restored.");
            }
            else
            {
                post.Title = revision.Title;
                post.ContentMarkdown = revision.ContentMarkdown;
                _showPendingChanges = false;
                await OnContentEditedAsync();
                _editContext?.NotifyValidationStateChanged();
                Toasts.ShowInfo($"Restored the revision from {TimeText(revision.SavedOn)}. " +
                    (IsPublished ? "Click Update to make it live." : "Save to keep it."));
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Loading revision {RevisionId} of post {PostId} failed.", revisionId, post.Id);
            Toasts.ShowError("The revision couldn't be loaded. Check your connection and try again.");
        }

        // Drop ?restore= from the address bar, so reloading the page doesn't restore the revision again.
        try
        {
            await JS.InvokeVoidAsync("history.replaceState", null, string.Empty, Navigation.ToAbsoluteUri($"admin/posts/{post.Id}").ToString());
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "Updating the address bar for post {PostId} failed.", post.Id);
        }

        StateHasChanged();
    }

    private async Task DiscardBackupAsync()
    {
        _backupOffer = null;
        if (Post is not null)
        {
            await Backups.RemoveAsync(Post.Id);
        }
    }

    /// <summary>
    /// Loads the staged (autosaved, not yet live) title and content of a published post into the editor. They
    /// are already stored on the server, so this doesn't count as unsaved; <b>Update</b> makes them live.
    /// </summary>
    private void RestorePendingChanges()
    {
        if (Post?.PendingChanges is not { } pending)
        {
            return;
        }

        Post.Title = pending.Title;
        Post.ContentMarkdown = pending.ContentMarkdown;
        _stats = ReadingTime.Calculate(Post.ContentMarkdown);
        _showPendingChanges = false;
    }

    private void DismissPendingChanges()
    {
        _showPendingChanges = false;
    }

    // ---- Leaving -------------------------------------------------------------------------------------------

    /// <summary>
    /// Programmatic navigation (and, in future Blazor versions, link clicks): asks before leaving unsaved changes.
    /// </summary>
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
    /// Called by admin.js for an in-app link clicked while there are unsaved changes. With static routing, link
    /// clicks use enhanced navigation, which never reaches <see cref="NavigationLock"/>, so the guard cancels the
    /// click and this decides whether to follow it.
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

    /// <summary>Tries to autosave first, then asks before leaving anything unsaved behind.</summary>
    private async Task<bool> CanLeaveAsync()
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        if (!_conflictUnresolved)
        {
            await AutosaveAsync();
            StateHasChanged();
        }

        return !HasUnsavedChanges || await _confirm.ConfirmAsync(
            "Leave without saving?",
            "Some changes haven't been saved. They're kept in this browser, and you can restore them when you open this post again.",
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
            // The beforeunload prompt and the local backup still protect the work.
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
    private (string Icon, string Text, string CssClass) StatusLine => _status switch
    {
        SaveStatus.Saving => ("bi-arrow-repeat", "Saving…", "text-body-secondary"),
        SaveStatus.Failed => ("bi-exclamation-triangle", "Not saved. Your changes are kept in this browser.", "text-danger"),
        SaveStatus.Conflict => ("bi-exclamation-triangle", "Not saved: this post was changed in another tab.", "text-danger"),
        SaveStatus.Invalid when HasUnsavedEdits => ("bi-exclamation-circle", $"Not saved yet: {_autosaveProblem ?? "fix the highlighted fields."}", "text-warning"),
        _ when HasUnsavedEdits => ("bi-pencil", "Unsaved changes", "text-body-secondary"),
        _ when IsNew => ("bi-file-earmark", "Not saved yet", "text-body-secondary"),
        _ when HasChangesNotLive && IsScheduled => ("bi-cloud-check", $"Saved {TimeText(_savedAt)}, not in the scheduled post yet", "text-warning"),
        _ when HasChangesNotLive => ("bi-cloud-check", $"Saved {TimeText(_savedAt)}, not live yet", "text-warning"),
        _ => ("bi-check2-circle", $"Saved {TimeText(_savedAt)}", "text-success")
    };

    /// <summary>A time as the author's local clock shows it, such as "10:42".</summary>
    private static string TimeText(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) ?? string.Empty;
    }

    /// <summary>
    /// When a scheduled post goes live, in the blog's time zone (the zone the schedule was picked in), such as
    /// "October 1, 2026 at 9:00 AM".
    /// </summary>
    private string ScheduledText
    {
        get
        {
            if (Post?.PublishedOn is not { } publishOn)
            {
                return string.Empty;
            }

            try
            {
                var local = _timeZoneId is null ? publishOn.ToLocalTime().DateTime : BlogTimeZone.ToLocalDateTime(publishOn, _timeZoneId);
                return string.Create(CultureInfo.CurrentCulture, $"{local:MMMM d, yyyy} at {local:t}");
            }
            catch (TimeZoneNotFoundException)
            {
                return string.Create(CultureInfo.CurrentCulture, $"{publishOn.ToLocalTime():MMMM d, yyyy} at {publishOn.ToLocalTime():t}");
            }
        }
    }

    /// <summary>A publish date in the blog's time zone, as used in the URL.</summary>
    private static string DateText(DateOnly? value)
    {
        return value?.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture) ?? string.Empty;
    }

    /// <summary>Whether a live post's slug differs from the stored one, so Update will add a redirect.</summary>
    private bool SlugWillRedirect => IsLive && _stored is not null && Post is not null
        && !string.IsNullOrWhiteSpace(Post.Slug) && Post.Slug != _stored.Slug;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _lifetime.Dispose();
        _autosaveTimer?.Dispose();
        _slugCheckCancellation?.Cancel();
        _slugCheckCancellation?.Dispose();

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

    /// <summary>What a save does.</summary>
    private enum SaveKind
    {
        /// <summary>Periodic or on-blur save: updates a draft, stages content on a published post.</summary>
        Autosave,

        /// <summary>Explicit save of a draft (keeps a manual revision).</summary>
        Draft,

        /// <summary>Makes a published post's changes live.</summary>
        Update,

        /// <summary>Saves, then publishes now.</summary>
        Publish,

        /// <summary>Saves, then publishes at the time picked in the schedule picker (design 6.3, A9).</summary>
        Schedule
    }

    /// <summary>State of the most recent save, for the status line.</summary>
    private enum SaveStatus
    {
        Idle,
        Saving,
        Saved,
        Invalid,
        Failed,
        Conflict
    }
}
