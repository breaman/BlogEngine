using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlogEngine.Client.Components;

/// <summary>
/// A modal that edits a media item's alt text and caption (design 6.6, M4, T2.8), validated by the shared
/// <c>MediaUpdateValidator</c> through Blazilla. Place one per page and call <see cref="EditAsync"/>.
/// </summary>
/// <example>
/// <code>
/// if (await _metadataDialog.EditAsync(item) is { } saved)
/// {
///     Replace(item, saved);
/// }
/// </code>
/// </example>
public sealed partial class MediaMetadataDialog : ComponentBase
{
    [Inject] private IMediaService MediaService { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;

    private readonly string _titleId = $"media-metadata-{Guid.NewGuid():N}";
    private MediaItemDto? _item;
    private MediaUpdateRequest? _model;
    private TaskCompletionSource<MediaItemDto?>? _pending;
    private string? _error;
    private bool _saving;

    /// <summary>Opens the dialog for <paramref name="item"/>; completes with the saved item, or null when cancelled.</summary>
    public Task<MediaItemDto?> EditAsync(MediaItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _pending?.TrySetResult(null);
        _pending = new TaskCompletionSource<MediaItemDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _item = item;
        _model = new MediaUpdateRequest { AltText = item.AltText, Caption = item.Caption };
        _error = null;
        StateHasChanged();

        return _pending.Task;
    }

    private async Task SaveAsync()
    {
        if (_item is null || _model is null)
        {
            return;
        }

        _saving = true;
        _error = null;
        try
        {
            switch (await MediaService.UpdateAsync(_item.Id, _model))
            {
                case MediaSaved saved:
                    Toasts.ShowSuccess($"Saved the details of {saved.Item.FileName}.");
                    Finish(saved.Item);
                    break;
                case MediaNotFound:
                    Toasts.ShowWarning($"{_item.FileName} no longer exists.");
                    Finish(null);
                    break;
                case MediaInvalid invalid:
                    _error = string.Join(" ", invalid.Errors.SelectMany(e => e.Value));
                    break;
            }
        }
        catch (HttpRequestException)
        {
            _error = "The server couldn't be reached. Check your connection and try again.";
        }
        finally
        {
            _saving = false;
        }
    }

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            Close();
        }
    }

    private void Close()
    {
        Finish(null);
    }

    private void Finish(MediaItemDto? result)
    {
        _item = null;
        _model = null;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(result);
    }
}