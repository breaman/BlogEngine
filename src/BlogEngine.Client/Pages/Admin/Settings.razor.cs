using BlogEngine.Client.Components;
using BlogEngine.Client.Services;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The site settings page at <c>/admin/settings</c> (design 13, O4, T1.15, T4.25): every setting of design 13 (identity
/// with the author avatar and favicon, reading, time zone and dates, comments, notifications, media, search engines and
/// sharing), plus the export download (T4.24).
/// </summary>
/// <remarks>
/// <para>
/// The whole <see cref="SiteSettingsDto"/> is loaded and saved. The server evicts its settings cache on save, so public
/// pages use the new values on their next request. The comment keyword blocklist lives on the comments page, next to the
/// rest of the blocklist.
/// </para>
/// <para>
/// The settings hold only the ids of their images, so the thumbnails are loaded from the media library alongside them
/// (<see cref="Images"/>) and carried into WebAssembly with the settings.
/// </para>
/// <para>
/// Changing the time zone makes the server recompute the dates in post URLs and redirect the old URLs (T1.16),
/// so the page warns about it before saving.
/// </para>
/// </remarks>
public partial class Settings : ComponentBase
{
    [Inject] private ISettingsService SettingsService { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;
    [Inject] private IMediaService MediaService { get; set; } = default!;

    /// <summary>The settings being edited; loaded while prerendering and restored in the browser.</summary>
    [PersistentState]
    public SiteSettingsDto? Model { get; set; }

    /// <summary>
    /// The time zone picker's options. Built on the server while prerendering, because WebAssembly may ship a
    /// smaller time zone database than the host whose clock the blog uses.
    /// </summary>
    [PersistentState]
    public List<TimeZoneChoice>? TimeZones { get; set; }

    /// <summary>The time zone as last saved, to warn when it is about to change.</summary>
    [PersistentState]
    public string? SavedTimeZoneId { get; set; }

    /// <summary>Thumbnails of the images the settings point at.</summary>
    [PersistentState]
    public SettingsImages? Images { get; set; }

    private MediaPicker _mediaPicker = default!;
    private string _renditionWidthsText = string.Empty;
    private EditContext? _editContext;
    private ValidationMessageStore? _serverErrors;
    private bool _saving;
    private string? _loadError;

    private bool TimeZoneChanging => Model is not null && SavedTimeZoneId is not null
        && !string.Equals(Model.TimeZoneId, SavedTimeZoneId, StringComparison.Ordinal);

    /// <summary>Loads the settings unless they were restored from the prerendered page.</summary>
    protected override async Task OnInitializedAsync()
    {
        try
        {
            Model ??= await SettingsService.GetAsync();
        }
        catch (HttpRequestException)
        {
            _loadError = "The settings couldn't be loaded. Reload the page to try again.";
            return;
        }

        SavedTimeZoneId ??= Model.TimeZoneId;
        TimeZones ??= [.. TimeZoneChoices.GetAll(TimeProvider.GetUtcNow(), Model.TimeZoneId)];
        AttachEditContext(Model);

        Images ??= await LoadImagesAsync(Model);
    }

    /// <summary>Loads the thumbnails of the settings' images; if they can't be loaded, the fields just show no thumbnail.</summary>
    private async Task<SettingsImages> LoadImagesAsync(SiteSettingsDto settings)
    {
        try
        {
            return new SettingsImages
            {
                AuthorAvatar = await LoadImageAsync(settings.AuthorAvatarMediaId),
                Favicon = await LoadImageAsync(settings.FaviconMediaId),
                DefaultSocialImage = await LoadImageAsync(settings.DefaultSocialImageMediaId)
            };
        }
        catch (HttpRequestException)
        {
            // The settings themselves loaded; missing thumbnails don't stop them from being edited.
            return new SettingsImages();
        }
    }

    private async Task<PostImageDto?> LoadImageAsync(int? mediaId)
    {
        return mediaId is { } id && await MediaService.GetMediaItemAsync(id) is { } item ? PostImageDto.From(item) : null;
    }

    private async Task ChooseAuthorAvatarAsync()
    {
        if (Model is not null && await _mediaPicker.PickAsync("Choose the author avatar") is { } item)
        {
            Model.AuthorAvatarMediaId = item.Id;
            Images ??= new SettingsImages();
            Images.AuthorAvatar = PostImageDto.From(item);
            _editContext?.NotifyFieldChanged(_editContext.Field(nameof(SiteSettingsDto.AuthorAvatarMediaId)));
        }
    }

    private void RemoveAuthorAvatar()
    {
        if (Model is not null)
        {
            Model.AuthorAvatarMediaId = null;
            Images?.AuthorAvatar = null;
        }
    }

    private async Task ChooseFaviconAsync()
    {
        if (Model is not null && await _mediaPicker.PickAsync("Choose the favicon") is { } item)
        {
            Model.FaviconMediaId = item.Id;
            Images ??= new SettingsImages();
            Images.Favicon = PostImageDto.From(item);
            _editContext?.NotifyFieldChanged(_editContext.Field(nameof(SiteSettingsDto.FaviconMediaId)));
        }
    }

    private void RemoveFavicon()
    {
        if (Model is not null)
        {
            Model.FaviconMediaId = null;
            Images?.Favicon = null;
        }
    }

    private async Task ChooseDefaultSocialImageAsync()
    {
        if (Model is not null && await _mediaPicker.PickAsync("Choose the default social image") is { } item)
        {
            Model.DefaultSocialImageMediaId = item.Id;
            Images ??= new SettingsImages();
            Images.DefaultSocialImage = PostImageDto.From(item);
            _editContext?.NotifyFieldChanged(_editContext.Field(nameof(SiteSettingsDto.DefaultSocialImageMediaId)));
        }
    }

    private void RemoveDefaultSocialImage()
    {
        if (Model is not null)
        {
            Model.DefaultSocialImageMediaId = null;
            Images?.DefaultSocialImage = null;
        }
    }

    /// <summary>
    /// Reads the rendition widths text box into the model. Text that isn't a list of numbers keeps an error on the field
    /// (which blocks saving) until it is fixed; ranges are then checked by the validator.
    /// </summary>
    private void SetRenditionWidths(ChangeEventArgs e)
    {
        if (Model is null || _editContext is null || _serverErrors is null)
        {
            return;
        }

        _renditionWidthsText = e.Value?.ToString() ?? string.Empty;
        var field = _editContext.Field(nameof(SiteSettingsDto.RenditionWidths));
        _serverErrors.Clear(field);

        if (RenditionWidthsText.TryParse(_renditionWidthsText, out var widths, out var error))
        {
            Model.RenditionWidths = [.. widths];
            _editContext.NotifyFieldChanged(field);
        }
        else
        {
            _serverErrors.Add(field, error!);
            _editContext.NotifyValidationStateChanged();
        }
    }

    /// <summary>Saves the settings; server-side validation errors are shown next to their fields.</summary>
    private async Task SaveAsync()
    {
        if (Model is null || _saving)
        {
            return;
        }

        _saving = true;
        _serverErrors?.Clear();
        try
        {
            await SettingsService.SaveAsync(Model);

            // Reload so the form shows the values as stored (trimmed text, sorted rendition widths).
            Model = await SettingsService.GetAsync();
            SavedTimeZoneId = Model.TimeZoneId;
            AttachEditContext(Model);
            Toasts.ShowSuccess("Settings saved.");
        }
        catch (ValidationException ex)
        {
            ShowServerErrors(ex);
            Toasts.ShowError("Some settings are invalid. Fix the highlighted fields and save again.");
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The settings couldn't be saved. Check your connection and try again.");
        }
        finally
        {
            _saving = false;
        }
    }

    private void AddSocialLink()
    {
        Model?.SocialLinks.Add(new SocialLinkDto());
    }

    private void RemoveSocialLink(SocialLinkDto link)
    {
        Model?.SocialLinks.Remove(link);
        _editContext?.NotifyValidationStateChanged();
    }

    /// <summary>A new edit context for a newly loaded model, with a store for errors reported by the server.</summary>
    private void AttachEditContext(SiteSettingsDto model)
    {
        _renditionWidthsText = RenditionWidthsText.Format(model.RenditionWidths);
        _editContext = new EditContext(model);
        _serverErrors = new ValidationMessageStore(_editContext);
        _editContext.OnFieldChanged += (_, e) => _serverErrors.Clear(e.FieldIdentifier);
    }

    /// <summary>Shows errors the server found (for example a time zone the server doesn't know).</summary>
    private void ShowServerErrors(ValidationException ex)
    {
        if (_editContext is null || _serverErrors is null)
        {
            return;
        }

        foreach (var failure in ex.Errors)
        {
            // Nested paths such as SocialLinks[0].Url aren't mapped back to fields; they still reach the summary.
            var field = string.IsNullOrEmpty(failure.PropertyName) || failure.PropertyName.Contains('.', StringComparison.Ordinal)
                ? new FieldIdentifier(_editContext.Model, string.Empty)
                : _editContext.Field(failure.PropertyName);
            _serverErrors.Add(field, failure.ErrorMessage);
        }

        _editContext.NotifyValidationStateChanged();
    }

    /// <summary>Thumbnails of the images chosen in the settings; <see langword="null"/> where none is chosen.</summary>
    public sealed class SettingsImages
    {
        /// <summary>The author avatar.</summary>
        public PostImageDto? AuthorAvatar { get; set; }

        /// <summary>The favicon.</summary>
        public PostImageDto? Favicon { get; set; }

        /// <summary>The default social sharing image.</summary>
        public PostImageDto? DefaultSocialImage { get; set; }
    }
}
