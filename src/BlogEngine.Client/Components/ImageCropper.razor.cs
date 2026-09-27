using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Media;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Components;

/// <summary>
/// Visual crop, rotate and flip of an image (design 9.2, M2, Q8, T2.9), wrapping the Cropper.js v2 web components
/// (<c>cropper-canvas</c>, <c>-image</c>, <c>-selection</c>, <c>-shade</c>, <c>-handle</c>) through
/// <c>wwwroot/js/cropper.js</c>.
/// </summary>
/// <remarks>
/// <para>
/// The author sees a scaled-down copy, but <see cref="OnChanged"/> reports the crop in <b>natural pixel coordinates
/// of the rotated and flipped original</b>: cropper.js reports the selection and the drawn image in CSS pixels, and
/// <see cref="MediaGeometry.ToNatural"/> maps one onto the other. That is exactly what the server applies after
/// rotating and flipping the full-size original, so the result matches what was selected.
/// </para>
/// <para>
/// Rotating or flipping resets the selection to the whole image. The toolbar offers 90° rotations, both flips, the
/// aspect ratios Free, Original, 1:1, 4:3, 3:2 and 16:9, and Reset; the live pixel size and a preview thumbnail of
/// the selection are shown beside the image.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;ImageCropper Src="api/admin/media/7/original" InitialOperations="Item.EditOperations" OnChanged="state => _crop = state" /&gt;
/// </code>
/// </example>
public sealed partial class ImageCropper : ComponentBase, IAsyncDisposable
{
    /// <summary>The aspect ratio presets; <c>Original</c> uses the rotated original's ratio.</summary>
    private static readonly IReadOnlyList<AspectPreset> AspectPresets =
    [
        new("Free", null),
        new("Original", 0),
        new("1:1", 1),
        new("4:3", 4 / 3.0),
        new("3:2", 3 / 2.0),
        new("16:9", 16 / 9.0)
    ];

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<ImageCropper> Logger { get; set; } = default!;

    /// <summary>URL of the image to edit; the media editor passes the untouched original.</summary>
    [Parameter, EditorRequired] public string Src { get; set; } = string.Empty;

    /// <summary>Alt text of the image, for assistive technology.</summary>
    [Parameter] public string? Alt { get; set; }

    /// <summary>Rotation, flips and crop to start from, such as the item's current edits.</summary>
    [Parameter] public MediaEditOperations? InitialOperations { get; set; }

    /// <summary>Raised whenever the rotation, flips or selection change.</summary>
    [Parameter] public EventCallback<ImageCropperState> OnChanged { get; set; }

    private ElementReference _host;
    private ElementReference _previewHost;
    private IJSObjectReference? _module;
    private IJSObjectReference? _cropper;
    private DotNetObjectReference<ImageCropper>? _selfReference;
    private ImageCropperState _state = new();
    private string _aspect = "Free";

    /// <summary>The locked aspect ratio (width / height), so the reported crop matches it exactly; null when free.</summary>
    private double? _aspectRatio;
    private string? _loadError;
    private bool _ready;
    private bool _disposed;

    /// <summary>Loads cropper.js and the image once the component runs in the browser.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var initial = InitialOperations;
        _state = new ImageCropperState
        {
            Rotate = MediaGeometry.NormalizeRotation(initial?.Rotate ?? 0),
            FlipHorizontal = initial?.FlipHorizontal ?? false,
            FlipVertical = initial?.FlipVertical ?? false
        };

        try
        {
            _selfReference = DotNetObjectReference.Create(this);
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/cropper.js");
            _cropper = await _module.InvokeAsync<IJSObjectReference>("createCropper", _host, _previewHost, _selfReference, new
            {
                src = Src,
                alt = Alt ?? string.Empty,
                rotate = _state.Rotate,
                flipHorizontal = _state.FlipHorizontal,
                flipVertical = _state.FlipVertical,
                crop = initial?.Crop is { } crop ? new { x = crop.X, y = crop.Y, width = crop.Width, height = crop.Height } : null
            });
            _ready = true;
        }
        catch (JSException ex)
        {
            Logger.LogError(ex, "The image editor failed to load {Src}.", Src);
            _loadError = "The image couldn't be loaded into the editor.";
        }

        StateHasChanged();
    }

    /// <summary>Called by cropper.js with the original's natural size, before anything is shown.</summary>
    [JSInvokable]
    public void OnImageLoaded(int naturalWidth, int naturalHeight)
    {
        _state.OriginalWidth = naturalWidth;
        _state.OriginalHeight = naturalHeight;
    }

    /// <summary>
    /// Called by cropper.js after the selection moved or resized, with the selection and the drawn image in CSS pixels
    /// relative to the cropper canvas.
    /// </summary>
    [JSInvokable]
    public async Task OnSelectionChanged(DisplayRect selection, DisplayRect image, CropperTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        _state.Rotate = transform.Rotate;
        _state.FlipHorizontal = transform.FlipHorizontal;
        _state.FlipVertical = transform.FlipVertical;
        var crop = SnapToInitialCrop(MediaGeometry.ToNatural(selection, image, _state.Width, _state.Height), image);
        _state.Crop = _aspectRatio is { } ratio ? MediaGeometry.FitAspectRatio(crop, ratio, _state.Width, _state.Height) : crop;

        await OnChanged.InvokeAsync(_state.Clone());
        StateHasChanged();
    }

    /// <summary>
    /// Returns the stored crop when <paramref name="crop"/> is within one display pixel of it. Restoring a crop maps it
    /// to screen pixels and back, which can move an edge by a natural pixel or two; without this, reopening an edited
    /// image would look like an unsaved change.
    /// </summary>
    private MediaCropRect SnapToInitialCrop(MediaCropRect crop, DisplayRect image)
    {
        if (InitialOperations is not { Crop: { } initial } operations
            || MediaGeometry.NormalizeRotation(operations.Rotate) != _state.Rotate
            || operations.FlipHorizontal != _state.FlipHorizontal
            || operations.FlipVertical != _state.FlipVertical
            || image.Width <= 0)
        {
            return crop;
        }

        var tolerance = (int)Math.Ceiling(_state.Width / image.Width) + 1;
        var isClose = Math.Abs(crop.X - initial.X) <= tolerance && Math.Abs(crop.Y - initial.Y) <= tolerance
            && Math.Abs(crop.Width - initial.Width) <= tolerance && Math.Abs(crop.Height - initial.Height) <= tolerance;

        return isClose ? new MediaCropRect { X = initial.X, Y = initial.Y, Width = initial.Width, Height = initial.Height } : crop;
    }

    private async Task RotateAsync(int degrees)
    {
        if (_cropper is not null)
        {
            await _cropper.InvokeAsync<CropperTransform>("rotate", degrees);
        }
    }

    private async Task FlipAsync(bool horizontal)
    {
        if (_cropper is not null)
        {
            await _cropper.InvokeAsync<CropperTransform>("flip", horizontal);
        }
    }

    private async Task ResetAsync()
    {
        if (_cropper is not null)
        {
            _aspect = "Free";
            _aspectRatio = null;
            await _cropper.InvokeAsync<CropperTransform>("reset");
        }
    }

    private async Task SetAspectAsync(AspectPreset preset)
    {
        if (_cropper is null)
        {
            return;
        }

        _aspect = preset.Name;
        var ratio = preset.Ratio switch
        {
            null => 0,
            0 when _state.Width > 0 && _state.Height > 0 => _state.Width / (double)_state.Height,
            var value => value.Value
        };
        _aspectRatio = ratio > 0 ? ratio : null;
        await _cropper.InvokeVoidAsync("setAspectRatio", ratio);
    }

    /// <summary>ARIA state attributes need the strings "true"/"false".</summary>
    private static string AriaBool(bool value)
    {
        return value ? "true" : "false";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_cropper is not null)
            {
                await _cropper.InvokeVoidAsync("dispose");
                await _cropper.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The page is being torn down; nothing left to release.
        }

        _selfReference?.Dispose();
    }

    /// <summary>An aspect ratio button: <see langword="null"/> is free, 0 means the original's ratio.</summary>
    private sealed record AspectPreset(string Name, double? Ratio);

    /// <summary>The rotation and flips cropper.js reports.</summary>
    /// <param name="Rotate">Clockwise rotation in degrees.</param>
    /// <param name="FlipHorizontal">Mirrored left to right.</param>
    /// <param name="FlipVertical">Mirrored top to bottom.</param>
    public sealed record CropperTransform(int Rotate, bool FlipHorizontal, bool FlipVertical);
}

/// <summary>
/// What <see cref="ImageCropper"/> reports: the rotation and flips, and the crop in natural pixels of the rotated and
/// flipped original.
/// </summary>
public sealed class ImageCropperState
{
    /// <summary>Width of the original as uploaded.</summary>
    public int OriginalWidth { get; set; }

    /// <summary>Height of the original as uploaded.</summary>
    public int OriginalHeight { get; set; }

    /// <summary>Clockwise rotation: 0, 90, 180 or 270.</summary>
    public int Rotate { get; set; }

    /// <summary>Mirrored left to right, after rotating.</summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>Mirrored top to bottom, after rotating.</summary>
    public bool FlipVertical { get; set; }

    /// <summary>The selection in natural pixels of the rotated original; <see langword="null"/> before the first report.</summary>
    public MediaCropRect? Crop { get; set; }

    /// <summary>Width of the rotated original.</summary>
    public int Width => MediaGeometry.RotatedSize(OriginalWidth, OriginalHeight, Rotate).Width;

    /// <summary>Height of the rotated original.</summary>
    public int Height => MediaGeometry.RotatedSize(OriginalWidth, OriginalHeight, Rotate).Height;

    /// <summary>Whether the selection covers the whole rotated image, so no crop is needed.</summary>
    public bool CropsNothing => Crop is null || (Crop.X == 0 && Crop.Y == 0 && Crop.Width == Width && Crop.Height == Height);

    /// <summary>A copy, so handlers can keep it while the cropper goes on changing.</summary>
    public ImageCropperState Clone()
    {
        return new ImageCropperState
        {
            OriginalWidth = OriginalWidth,
            OriginalHeight = OriginalHeight,
            Rotate = Rotate,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            Crop = Crop is null ? null : new MediaCropRect { X = Crop.X, Y = Crop.Y, Width = Crop.Width, Height = Crop.Height }
        };
    }

    /// <summary>The operations to send to the server, with an optional final size.</summary>
    public MediaEditOperations ToOperations(MediaSize? resize)
    {
        var cropSize = Crop is null ? new MediaSize { Width = Width, Height = Height } : new MediaSize { Width = Crop.Width, Height = Crop.Height };
        return new MediaEditOperations
        {
            Rotate = Rotate,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            Crop = CropsNothing ? null : Crop,
            Resize = resize is null || (resize.Width == cropSize.Width && resize.Height == cropSize.Height) ? null : resize
        };
    }
}