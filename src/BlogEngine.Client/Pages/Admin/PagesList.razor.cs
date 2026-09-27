using System.Globalization;

using BlogEngine.Client.Components;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The admin pages list at <c>/admin/pages</c> (design 7.3, A17): every standalone page with its status and navigation
/// position, links to create one (blank or from a template such as Privacy), and edit, view and delete actions.
/// </summary>
/// <remarks>
/// A blog has a handful of pages, so the list isn't paged or filtered. The list loaded while prerendering is carried
/// into WebAssembly with <see cref="PersistentStateAttribute"/>, so hydration doesn't fetch it again.
/// </remarks>
public partial class PagesList : ComponentBase
{
    [Inject] private IPageAdminService PageService { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;

    /// <summary>The pages, in navigation order.</summary>
    [PersistentState]
    public IReadOnlyList<PageSummaryDto>? Pages { get; set; }

    private ConfirmDialog _confirm = default!;
    private string? _loadError;
    private int? _busyPageId;

    /// <summary>Loads the pages unless they were restored from prerendering.</summary>
    protected override async Task OnInitializedAsync()
    {
        if (Pages is null)
        {
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loadError = null;
        try
        {
            Pages = await PageService.GetPagesAsync();
        }
        catch (HttpRequestException)
        {
            _loadError = "The pages couldn't be loaded. Check your connection and try again.";
        }
    }

    /// <summary>Deletes a page permanently after confirming, then refreshes the list.</summary>
    private async Task DeleteAsync(PageSummaryDto page)
    {
        var confirmed = await _confirm.ConfirmAsync(
            "Delete page?",
            $"\"{page.Title}\" will be deleted permanently and its URL will stop working. This can't be undone.",
            "Delete",
            "btn-danger");
        if (!confirmed)
        {
            return;
        }

        _busyPageId = page.Id;
        try
        {
            if (await PageService.DeleteAsync(page.Id))
            {
                Toasts.ShowSuccess($"\"{page.Title}\" was deleted.");
            }
            else
            {
                Toasts.ShowWarning($"\"{page.Title}\" no longer exists.");
            }
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The server couldn't be reached. Check your connection and try again.");
        }
        finally
        {
            _busyPageId = null;
        }

        await LoadAsync();
    }

    /// <summary>A last-saved time as a short date in the author's local time.</summary>
    private static string DateText(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? "—";
    }
}