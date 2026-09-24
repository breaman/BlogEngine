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
/// and every post that uses the image is re-rendered with the new <c>?v=</c>. <b>Cancel</b> returns to the library
/// without saving. (Save as copy and Revert come with T4.18.)
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

        _saving = true;
        try
        {
            switch (await MediaService.EditAsync(Item.Id, _crop.ToOperations(_resize)))
            {
                case MediaSaved saved:
                    Item = saved.Item;
                    ResetForm();
                    var posts = saved.Item.UsageCount;
                    Toasts.ShowSuccess(posts > 0
                        ? $"Saved version {saved.Item.Version}. {posts} {(posts == 1 ? "post shows" : "posts show")} the new version."
                        : $"Saved version {saved.Item.Version}.");
                    break;
                case MediaNotFound:
                    ItemMissing = true;
                    Item = null;
                    break;
                case MediaInvalid invalid:
                    Toasts.ShowError(string.Join(" ", invalid.Errors.SelectMany(e => e.Value)), "The image wasn't saved");
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
