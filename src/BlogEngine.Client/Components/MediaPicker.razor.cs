using BlogEngine.Client.Services;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Markdown;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlogEngine.Client.Components;

/// <summary>
/// The Markdown editor's insert-image dialog (design 9.5, A5, M3, M4, T2.11): a searchable library grid with a
/// "Recently used" row, an Upload tab, and a details step for alt text (pre-filled from the library, required unless
/// the image is decorative), caption, size and alignment. <see cref="PickAsync"/> uses the same dialog to choose one
/// image without the details step, such as a post's cover (A15).
/// </summary>
/// <remarks>
/// It produces standard Markdown through <see cref="MediaMarkdown"/>, such as
/// <c>![Sunset](/media/ab12cd34ef56/sunset.jpg "At dusk"){.img-medium}</c>, and adds the item to
/// <see cref="MediaLookupCache"/> so the preview renders it straight away, exactly as it will be published.
/// </remarks>
/// <example>
/// <code>
/// if (await _mediaPicker.ShowAsync() is { } markdown)
/// {
///     await _editor.InsertBlockAsync(markdown);
/// }
/// </code>
/// </example>
public sealed partial class MediaPicker : ComponentBase
{
    /// <summary>Images per library page.</summary>
    private const int PageSize = 24;

    private static readonly IReadOnlyList<(MediaImageSize Value, string Label)> Sizes =
    [
        (MediaImageSize.Full, "Full width"),
        (MediaImageSize.Medium, "Medium"),
        (MediaImageSize.Small, "Small")
    ];

    /// <summary>The details form's validator, passed to <c>&lt;FluentValidator&gt;</c> directly (it isn't in DI).</summary>
    private static readonly InsertDetailsValidator DetailsValidator = new();

    [Inject] private IMediaService MediaService { get; set; } = default!;
    [Inject] private MediaLookupCache MediaLookup { get; set; } = default!;
    [Inject] private RecentMediaStore RecentMedia { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;
    [Inject] private ILogger<MediaPicker> Logger { get; set; } = default!;

    private readonly string _titleId = $"media-picker-{Guid.NewGuid():N}";
    private readonly List<MediaItemDto> _items = [];
    private readonly List<MediaItemDto> _uploadedInBatch = [];
    private List<MediaItemDto> _recent = [];
    private TaskCompletionSource<string?>? _pending;
    private TaskCompletionSource<MediaItemDto?>? _pendingPick;
    private string? _pickTitle;
    private InsertDetails _details = new();
    private MediaItemDto? _selected;
    private PickerTab _tab = PickerTab.Library;
    private string? _search;
    private string? _appliedSearch;
    private string? _loadError;
    private int _page;
    private bool _hasMore;
    private bool _loading;
    private bool _inserting;
    private bool _open;

    private string Markdown => _selected is null ? string.Empty : BuildMarkdown(_selected, _details);

    /// <summary>
    /// Opens the dialog and completes with the Markdown to insert, or <see langword="null"/> when cancelled. Opening
    /// it again while it is open cancels the earlier request.
    /// </summary>
    public async Task<string?> ShowAsync()
    {
        CancelPending();
        _pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = _pending;
        _pickTitle = null;

        await OpenAsync();
        return await pending.Task;
    }

    /// <summary>
    /// Opens the dialog to choose one library image (or upload one) without the insert details, for example a post's
    /// cover image (A15). Completes with the item, or <see langword="null"/> when cancelled.
    /// </summary>
    /// <param name="title">The dialog title, such as "Choose a cover image".</param>
    public async Task<MediaItemDto?> PickAsync(string title)
    {
        CancelPending();
        _pendingPick = new TaskCompletionSource<MediaItemDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = _pendingPick;
        _pickTitle = title;

        await OpenAsync();
        return await pending.Task;
    }

    /// <summary>Shows the library tab and loads its first page and the recently used row.</summary>
    private async Task OpenAsync()
    {
        _open = true;
        _selected = null;
        _tab = PickerTab.Library;
        _search = _appliedSearch = null;
        StateHasChanged();

        await Task.WhenAll(LoadPageAsync(reset: true), LoadRecentAsync());
        StateHasChanged();
    }

    /// <summary>Completes an earlier request that is still open as cancelled.</summary>
    private void CancelPending()
    {
        _pending?.TrySetResult(null);
        _pending = null;
        _pendingPick?.TrySetResult(null);
        _pendingPick = null;
    }

    private async Task SearchAsync()
    {
        _appliedSearch = string.IsNullOrWhiteSpace(_search) ? null : _search.Trim();
        await LoadPageAsync(reset: true);
    }

    private Task LoadMoreAsync()
    {
        return LoadPageAsync(reset: false);
    }

    private async Task LoadPageAsync(bool reset)
    {
        if (reset)
        {
            _items.Clear();
            _page = 0;
        }

        _loading = true;
        _loadError = null;
        try
        {
            var result = await MediaService.GetMediaAsync(new MediaListQuery { Search = _appliedSearch, Page = _page + 1, PageSize = PageSize });
            _page = result.Page;
            _items.AddRange(result.Items.Where(item => _items.All(existing => existing.Id != item.Id)));
            _hasMore = result.Page < result.TotalPages;
        }
        catch (HttpRequestException)
        {
            _loadError = "The media library couldn't be loaded. Check your connection and try again.";
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Loads the "Recently used" row, skipping items that were deleted since.</summary>
    private async Task LoadRecentAsync()
    {
        try
        {
            var ids = await RecentMedia.LoadAsync();
            var items = await Task.WhenAll(ids.Select(id => MediaService.GetMediaItemAsync(id)));
            _recent = [.. items.OfType<MediaItemDto>()];
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Loading the recently used media failed.");
            _recent = [];
        }
    }

    private async Task Select(MediaItemDto item)
    {
        if (_pendingPick is { } pick)
        {
            // Choosing only: no details step. Remember it for the "Recently used" row like an inserted image.
            await RecentMedia.AddAsync(item);
            _open = false;
            _pendingPick = null;
            pick.TrySetResult(item);
            return;
        }

        _selected = item;
        _details = new InsertDetails
        {
            AltText = item.AltText,
            Caption = item.Caption ?? string.Empty,
            SaveAltTextToLibrary = string.IsNullOrWhiteSpace(item.AltText)
        };
    }

    private void OnUploaded(MediaItemDto item)
    {
        _uploadedInBatch.Add(item);
        _items.Insert(0, item);
    }

    /// <summary>A single upload goes straight to its details; several go back to the grid, where they appear first.</summary>
    private async Task OnUploadBatchCompleted()
    {
        if (_uploadedInBatch.Count == 1)
        {
            await Select(_uploadedInBatch[0]);
        }
        else if (_uploadedInBatch.Count > 1)
        {
            _tab = PickerTab.Library;
        }

        _uploadedInBatch.Clear();
    }

    /// <summary>Saves the alt text to the library when asked, remembers the image, and hands back the Markdown.</summary>
    private async Task InsertAsync()
    {
        if (_selected is not { } item)
        {
            return;
        }

        _inserting = true;
        try
        {
            var altText = _details.IsDecorative ? string.Empty : _details.AltText.Trim();
            if (_details.SaveAltTextToLibrary && altText.Length > 0 && string.IsNullOrWhiteSpace(item.AltText)
                && await MediaService.UpdateAsync(item.Id, new MediaUpdateRequest { AltText = altText, Caption = item.Caption }) is MediaSaved saved)
            {
                item = saved.Item;
            }

            MediaLookup.Remember(item);
            await RecentMedia.AddAsync(item);
            Finish(BuildMarkdown(item, _details));
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The alt text couldn't be saved to the library. Check your connection and try again.");
        }
        finally
        {
            _inserting = false;
        }
    }

    private static string BuildMarkdown(MediaItemDto item, InsertDetails details)
    {
        return MediaMarkdown.Image(item.PublicId, item.FileName, details.IsDecorative ? string.Empty : details.AltText,
            details.Caption, details.Size, details.Center);
    }

    private void Back()
    {
        _selected = null;
    }

    private void Cancel()
    {
        Finish(null);
    }

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            Cancel();
        }
    }

    private void Finish(string? markdown)
    {
        _open = false;
        _selected = null;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(markdown);

        var pick = _pendingPick;
        _pendingPick = null;
        pick?.TrySetResult(null);
    }

    private static string AriaBool(bool value)
    {
        return value ? "true" : "false";
    }

    private enum PickerTab
    {
        Library,
        Upload
    }

    /// <summary>The details step's form.</summary>
    private sealed class InsertDetails
    {
        public string AltText { get; set; } = string.Empty;

        public bool IsDecorative { get; set; }

        public bool SaveAltTextToLibrary { get; set; }

        public string Caption { get; set; } = string.Empty;

        public MediaImageSize Size { get; set; } = MediaImageSize.Full;

        public bool Center { get; set; }
    }

    /// <summary>Alt text is required unless the image is marked decorative (design 9.5, M4).</summary>
    private sealed class InsertDetailsValidator : AbstractValidator<InsertDetails>
    {
        public InsertDetailsValidator()
        {
            RuleFor(d => d.AltText)
                .NotEmpty().When(d => !d.IsDecorative)
                .WithMessage("Describe the image, or mark it as decorative.")
                .MaximumLength(FieldLengths.AltText)
                .WithName("Alt text");

            RuleFor(d => d.Caption).MaximumLength(FieldLengths.Caption);
        }
    }
}