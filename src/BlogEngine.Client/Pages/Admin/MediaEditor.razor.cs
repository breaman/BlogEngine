using System.Globalization;
using System.Text.Json;

using BlogEngine.Client.Components;
using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Media;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The media editor at <c>/admin/media/{id}</c> (design 7.3, 9.2, M2, T2.10): crop, rotate and flip with
/// <see cref="ImageCropper"/>, resize with an aspect lock and no upscaling, plus alt text, caption, file details,
/// the posts that use the image, and delete.
/// </summary>
/// <remarks>
/// <para>
/// The cropper always shows the original upload and starts from the stored edits, because the server applies the
/// operations to the original (design 9.2). <b>Save</b> sends only the operations; the server writes a new version,
/// and every post that uses the image is re-rendered with the new <c>?v=</c>. <b>Save as copy</b> stores the result as a
/// new library item instead and opens it, leaving the posts that use this one alone. <b>Revert to original</b> undoes
/// every edit (after confirming when posts use the image), and <b>Cancel</b> returns to the library without saving
/// (design 9.2, M7, T4.18).
/// </para>
/// <para>The item loaded while prerendering is carried into WebAssembly with <see cref="PersistentStateAttribute"/>.</para>
/// </remarks>
public partial class MediaEditor : ComponentBase
{
    [Inject] private IMediaService MediaService { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    /// <summary>The media item id from the route.</summary>
    [Parameter] public int Id { get; set; }

    /// <summary>The item being edited.</summary>
    [PersistentState]
    public MediaItemDto? Item { get; set; }

    /// <summary>Whether the requested item doesn't exist.</summary>
    [PersistentState]
    public bool ItemMissing { get; set; }

    private ConfirmDialog _confirm = default!;
    private MediaUpdateRequest _metadata = new();
    private ImageCropperState? _crop;
    private MediaSize _resize = new();
    private bool _resizeTouched;
    private bool _lockAspect = true;
    private bool _saving;
    private bool _savingMetadata;
    private bool _interactive;
    private string? _loadError;
    private int? _loadedId;

    private string OriginalUrl => string.Create(CultureInfo.InvariantCulture, $"{ClientMediaService.BaseUri}/{Id}/original");

    /// <summary>The selected area's size: the most the image can be resized to.</summary>
    private MediaSize CropSize => _crop switch
    {
        { Crop: { } crop } => new MediaSize { Width = crop.Width, Height = crop.Height },
        { } state => new MediaSize { Width = state.Width, Height = state.Height },
        _ => new MediaSize { Width = Item?.Width ?? 0, Height = Item?.Height ?? 0 }
    };

    /// <summary>Save is available once the cropper reported, when the result differs from the stored edits.</summary>
    private bool CanSave => _interactive && !_saving && _crop is not null && Item is not null
        && !SameOperations(_crop.ToOperations(_resize), Item.EditOperations);

    /// <summary>Save as copy is available once the cropper reported; unlike Save it also works without changes (a duplicate).</summary>
    private bool CanSaveCopy => _interactive && !_saving && _crop is not null && Item is not null;

    /// <summary>Loads the item unless restored state already belongs to this route.</summary>
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedId == Id)
        {
            return;
        }

        var firstLoad = _loadedId is null;
        _loadedId = Id;
        if (firstLoad && (ItemMissing || Item?.Id == Id))
        {
            ResetForm();
            return;
        }

        await LoadAsync();
    }

    /// <summary>Buttons only work once WebAssembly runs.</summary>
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            _interactive = true;
            StateHasChanged();
        }
    }

    private async Task LoadAsync()
    {
        _loadError = null;
        try
        {
            Item = await MediaService.GetMediaItemAsync(Id);
            ItemMissing = Item is null;
            ResetForm();
        }
        catch (HttpRequestException)
        {
            _loadError = "The image couldn't be loaded. Check your connection and try again.";
        }
    }

    /// <summary>Fills the details form and resize fields from <see cref="Item"/>.</summary>
    private void ResetForm()
    {
        _metadata = new MediaUpdateRequest { AltText = Item?.AltText ?? string.Empty, Caption = Item?.Caption };
        _crop = null;
        _resizeTouched = Item?.EditOperations?.Resize is not null;
        _resize = Item?.EditOperations?.Resize is { } resize
            ? new MediaSize { Width = resize.Width, Height = resize.Height }
            : new MediaSize { Width = Item?.Width ?? 0, Height = Item?.Height ?? 0 };
    }

    /// <summary>Follows the selection: the resize fields track its size until the author types their own.</summary>
    private void OnCropChanged(ImageCropperState state)
    {
        var first = _crop is null;
        _crop = state;
        var bounds = CropSize;

        if (!_resizeTouched || bounds.Width <= 0)
        {
            _resize = bounds;
        }
        else if (!first || _resize.Width > bounds.Width || _resize.Height > bounds.Height)
        {
            // Keep the chosen width where possible, but never larger than the new selection.
            _resize = _lockAspect ? MediaGeometry.ScaleToWidth(bounds, _resize.Width) : new MediaSize
            {
                Width = Math.Min(_resize.Width, bounds.Width),
                Height = Math.Min(_resize.Height, bounds.Height)
            };
        }
    }

    private void OnResizeWidthChanged(ChangeEventArgs e)
    {
        if (TryReadPixels(e, out var width))
        {
            _resizeTouched = true;
            _resize = _lockAspect
                ? MediaGeometry.ScaleToWidth(CropSize, width)
                : new MediaSize { Width = Math.Clamp(width, 1, CropSize.Width), Height = _resize.Height };
        }
    }

    private void OnResizeHeightChanged(ChangeEventArgs e)
    {
        if (TryReadPixels(e, out var height))
        {
            _resizeTouched = true;
            _resize = _lockAspect
                ? MediaGeometry.ScaleToHeight(CropSize, height)
                : new MediaSize { Width = _resize.Width, Height = Math.Clamp(height, 1, CropSize.Height) };
        }
    }

    private static bool TryReadPixels(ChangeEventArgs e, out int value)
    {
        return int.TryParse(e.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    /// <summary>Sends the operations; the server applies them to the original and stores a new version.</summary>
    private async Task SaveEditsAsync()
    {
        if (_crop is null || Item is null)
        {
            return;
        }

        await RunSaveAsync(() => MediaService.EditAsync(Item.Id, _crop.ToOperations(_resize)), "The image wasn't saved", saved =>
        {
            Item = saved;
            ResetForm();
            var posts = saved.UsageCount;
            Toasts.ShowSuccess(posts > 0
                ? $"Saved version {saved.Version}. {posts} {(posts == 1 ? "post shows" : "posts show")} the new version."
                : $"Saved version {saved.Version}.");
        });
    }

    /// <summary>Stores the current selection as a new library item and opens it (design 9.2).</summary>
    private async Task SaveAsCopyAsync()
    {
        if (_crop is null || Item is null)
        {
            return;
        }

        await RunSaveAsync(() => MediaService.SaveAsCopyAsync(Item.Id, _crop.ToOperations(_resize)), "The copy wasn't saved", copy =>
        {
            Toasts.ShowSuccess($"Saved a copy as {copy.FileName}. Posts using the original are unchanged.");
            Navigation.NavigateTo($"admin/media/{copy.Id}");
        });
    }

    /// <summary>Undoes every edit, after confirming when posts would change with it (M7).</summary>
    private async Task RevertAsync()
    {
        if (Item is null)
        {
            return;
        }

        var posts = Item.UsageCount;
        if (posts > 0 && !await _confirm.ConfirmAsync(
                "Revert to the original?",
                $"Every crop, rotation and resize will be undone, and the {(posts == 1 ? "post that uses" : $"{posts} posts that use")} this image will show the original.",
                "Revert",
                "btn-danger"))
        {
            return;
        }

        await RunSaveAsync(() => MediaService.RevertAsync(Item.Id), "The image wasn't reverted", reverted =>
        {
            Item = reverted;
            ResetForm();
            Toasts.ShowSuccess($"Reverted to the original ({reverted.Width} × {reverted.Height}) as version {reverted.Version}.");
        });
    }

    /// <summary>Runs a save with the buttons disabled and reports its outcome.</summary>
    private async Task RunSaveAsync(Func<Task<MediaSaveResult>> save, string failureTitle, Action<MediaItemDto> onSaved)
    {
        _saving = true;
        try
        {
            switch (await save())
            {
                case MediaSaved saved:
                    onSaved(saved.Item);
                    break;
                case MediaNotFound:
                    ItemMissing = true;
                    Item = null;
                    break;
                case MediaInvalid invalid:
                    Toasts.ShowError(string.Join(" ", invalid.Errors.SelectMany(e => e.Value)), failureTitle);
                    break;
            }
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The server couldn't be reached. Check your connection and try again.");
        }
        finally
        {
            _saving = false;
        }
    }

    private void Cancel()
    {
        Navigation.NavigateTo("admin/media");
    }

    private async Task SaveMetadataAsync()
    {
        if (Item is null)
        {
            return;
        }

        _savingMetadata = true;
        try
        {
            switch (await MediaService.UpdateAsync(Item.Id, _metadata))
            {
                case MediaSaved saved:
                    Item.AltText = saved.Item.AltText;
                    Item.Caption = saved.Item.Caption;
                    Toasts.ShowSuccess("Saved the alt text and caption.");
                    break;
                case MediaNotFound:
                    ItemMissing = true;
                    Item = null;
                    break;
                case MediaInvalid invalid:
                    Toasts.ShowError(string.Join(" ", invalid.Errors.SelectMany(e => e.Value)));
                    break;
            }
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The server couldn't be reached. Check your connection and try again.");
        }
        finally
        {
            _savingMetadata = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (Item is not null && await MediaDeletion.ConfirmAndDeleteAsync(_confirm, MediaService, Toasts, Item))
        {
            Navigation.NavigateTo("admin/media");
        }
    }

    /// <summary>Whether two sets of operations produce the same image (null means unedited).</summary>
    private static bool SameOperations(MediaEditOperations proposed, MediaEditOperations? stored)
    {
        return JsonSerializer.Serialize(proposed.IsIdentity ? null : proposed)
            == JsonSerializer.Serialize(stored is null || stored.IsIdentity ? null : stored);
    }
}