using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The site settings page at <c>/admin/settings</c> (design 13, O4, T1.15): identity, reading, time zone,
/// comment policy and the "discourage search engines" switch.
/// </summary>
/// <remarks>
/// <para>
/// The whole <see cref="SiteSettingsDto"/> is loaded and saved, so settings this page doesn't show yet (media,
/// notifications, T4.25) are sent back unchanged. The server evicts its settings cache on save, so public pages
/// use the new values on their next request.
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
}
