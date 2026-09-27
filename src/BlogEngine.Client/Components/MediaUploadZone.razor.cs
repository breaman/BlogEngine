using System.Net;

using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Security;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace BlogEngine.Client.Components;

/// <summary>
/// Multi-file drag-and-drop upload with a progress bar per file (design 9.1, T2.8), used by the media library and
/// the media picker's Upload tab.
/// </summary>
/// <remarks>
/// <para>
/// <c>wwwroot/js/media.js</c> keeps the files and sends each one in its own request to
/// <c>POST /api/admin/media</c>, so progress is per file and one failure doesn't affect the others. The browser's
/// <see cref="HttpClient"/> can't report upload progress, which is why the upload isn't done from .NET.
/// </para>
/// <para>
/// Files over the "max upload size" setting are rejected here, before any bytes are sent; the server checks again.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;MediaUploadZone OnUploaded="item => _items.Insert(0, item)" OnBatchCompleted="ReloadAsync" /&gt;
/// </code>
/// </example>
public sealed partial class MediaUploadZone : ComponentBase, IAsyncDisposable
{
    /// <summary>The <c>accept</c> filter of the file chooser (Q6).</summary>
    public const string AcceptedTypes = "image/jpeg,image/png,image/gif,image/webp";

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ISettingsService Settings { get; set; } = default!;
    [Inject] private AntiforgeryStateProvider Antiforgery { get; set; } = default!;
    [Inject] private ILogger<MediaUploadZone> Logger { get; set; } = default!;

    /// <summary>Raised for every file that was added to the library.</summary>
    [Parameter] public EventCallback<MediaItemDto> OnUploaded { get; set; }

    /// <summary>Raised when every queued file has finished, successfully or not.</summary>
    [Parameter] public EventCallback OnBatchCompleted { get; set; }

    /// <summary>A smaller layout for the media picker.</summary>
    [Parameter] public bool Compact { get; set; }

    private readonly List<UploadRow> _uploads = [];
    private ElementReference _dropZone;
    private ElementReference _fileInput;
    private IJSObjectReference? _module;
    private IJSObjectReference? _uploader;
    private DotNetObjectReference<MediaUploadZone>? _selfReference;
    private bool _ready;
    private bool _disposed;

    /// <summary>The per-file limit from the settings, once loaded.</summary>
    private int? MaxUploadMegabytes { get; set; }

    private bool IsUploading => _uploads.Any(u => u.State == UploadState.Uploading);

    /// <summary>Starts media.js once the component runs in the browser.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            MaxUploadMegabytes = (await Settings.GetAsync()).MaxUploadSizeMegabytes;
        }
        catch (HttpRequestException ex)
        {
            // The server still enforces the limit; only the early check is lost.
            Logger.LogWarning(ex, "Loading the upload size limit failed.");
        }

        try
        {
            _selfReference = DotNetObjectReference.Create(this);
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/media.js");
            _uploader = await _module.InvokeAsync<IJSObjectReference>("createUploader", _dropZone, _fileInput, _selfReference, new
            {
                url = ClientMediaService.BaseUri,
                headerName = AntiforgeryHeaders.RequestToken,
                token = Antiforgery.GetAntiforgeryToken()?.Value
            });
            _ready = true;
        }
        catch (JSException ex)
        {
            Logger.LogError(ex, "The media uploader failed to start.");
        }

        StateHasChanged();
    }

    /// <summary>
    /// Called by media.js with the dropped or chosen files. Adds a row for each, rejects files over the size limit,
    /// and returns the ids of the files to upload.
    /// </summary>
    [JSInvokable]
    public List<string> OnFilesQueued(List<QueuedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var maxBytes = MaxUploadMegabytes * 1024L * 1024L;
        var accepted = new List<string>(files.Count);
        foreach (var file in files)
        {
            var row = new UploadRow(file.Id, file.Name);
            if (file.Size > maxBytes)
            {
                row.Fail($"This file is {MediaFormat.Size(file.Size)}; the limit is {MaxUploadMegabytes} MB.");
            }
            else
            {
                accepted.Add(file.Id);
            }

            _uploads.Add(row);
        }

        StateHasChanged();
        return accepted;
    }

    /// <summary>Called by media.js as bytes are sent.</summary>
    [JSInvokable]
    public void OnUploadProgress(string id, double loaded, double total)
    {
        if (Find(id) is { } row && total > 0)
        {
            row.Percent = (int)Math.Clamp(Math.Round(loaded / total * 100), 0, 100);
            StateHasChanged();
        }
    }

    /// <summary>Called by media.js when a request finished, with its status code (0 for a network error) and body.</summary>
    [JSInvokable]
    public async Task OnUploadFinished(string id, int status, string body)
    {
        if (Find(id) is not { } row)
        {
            return;
        }

        var result = MediaUploadResponse.ReadResult(status, body);
        if (result is { Item: { } item })
        {
            row.Complete(item.FileName, result.Duplicates.FirstOrDefault());
            await OnUploaded.InvokeAsync(item);
        }
        else
        {
            if (status == (int)HttpStatusCode.OK && result is null)
            {
                Logger.LogWarning("The upload response for {FileName} couldn't be read.", row.Name);
            }

            row.Fail(result?.Error ?? MediaUploadResponse.DescribeFailure(status, body));
        }

        if (!IsUploading)
        {
            await OnBatchCompleted.InvokeAsync();
        }

        StateHasChanged();
    }

    private async Task ChooseFilesAsync()
    {
        if (_uploader is not null)
        {
            await _uploader.InvokeVoidAsync("openFilePicker");
        }
    }

    private void ClearFinished()
    {
        _uploads.RemoveAll(u => u.State != UploadState.Uploading);
    }

    private UploadRow? Find(string id)
    {
        return _uploads.Find(u => u.Id == id);
    }

    private static string UploadStatusText(UploadRow upload)
    {
        return upload.State switch
        {
            UploadState.Uploading => upload.Percent < 100 ? $"{upload.Percent}%" : "Processing…",
            UploadState.Done => "Uploaded",
            _ => "Failed"
        };
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
            if (_uploader is not null)
            {
                await _uploader.InvokeVoidAsync("dispose");
                await _uploader.DisposeAsync();
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

    /// <summary>A file media.js is about to upload.</summary>
    /// <param name="Id">The id media.js gave it.</param>
    /// <param name="Name">The file name.</param>
    /// <param name="Size">The size in bytes.</param>
    public sealed record QueuedFile(string Id, string Name, long Size);

    private enum UploadState
    {
        Uploading,
        Done,
        Failed
    }

    /// <summary>One row of the progress list.</summary>
    private sealed class UploadRow(string id, string name)
    {
        public string Id { get; } = id;

        public string Name { get; private set; } = name;

        public UploadState State { get; private set; } = UploadState.Uploading;

        public int Percent { get; set; }

        public string? Error { get; private set; }

        public MediaItemDto? DuplicateOf { get; private set; }

        public void Complete(string storedName, MediaItemDto? duplicateOf)
        {
            State = UploadState.Done;
            Name = storedName;
            DuplicateOf = duplicateOf;
        }

        public void Fail(string error)
        {
            State = UploadState.Failed;
            Error = error;
        }
    }
}