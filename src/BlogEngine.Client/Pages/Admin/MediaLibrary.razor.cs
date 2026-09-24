using System.Globalization;

using BlogEngine.Client.Components;
using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The media library at <c>/admin/media</c> (design 7.3, 9, M1, T2.8): multi-file drag-and-drop upload with progress,
/// a searchable grid with an "Unused" filter, alt text and caption editing, and delete with a "Used in N posts"
/// warning.
/// </summary>
/// <remarks>
/// Like the posts list, the filters live in the query string (<c>?search=sunset&amp;unused=true&amp;page=2</c>), and the
/// page loaded while prerendering is carried into WebAssembly with <see cref="PersistentStateAttribute"/>, tagged with
/// the query it belongs to.
/// </remarks>
public partial class MediaLibrary : ComponentBase
{
    [Inject] private IMediaService MediaService { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    /// <summary>Text to find in file names, alt text and captions.</summary>
    [SupplyParameterFromQuery(Name = "search")]
    public string? SearchParameter { get; set; }

    /// <summary>Only images no post uses.</summary>
    [SupplyParameterFromQuery(Name = "unused")]
    public bool? UnusedParameter { get; set; }

    /// <summary>1-based page number.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    public int? PageParameter { get; set; }

    /// <summary>The loaded page of images.</summary>
    [PersistentState]
    public PagedResult<MediaItemDto>? Result { get; set; }

    /// <summary>The query <see cref="Result"/> was loaded for.</summary>
    [PersistentState]
    public string? LoadedQuery { get; set; }

    private MediaMetadataDialog _metadataDialog = default!;
    private ConfirmDialog _confirm = default!;
    private string? _searchFilter;
    private bool _loading;
    private string? _loadError;
    private int? _busyItemId;

    private bool Unused => UnusedParameter == true;

    private bool HasFilters => Unused || !string.IsNullOrWhiteSpace(SearchParameter);

    /// <summary>Loads the page for the current query unless the loaded (or restored) page already matches it.</summary>
    protected override async Task OnParametersSetAsync()
    {
        _searchFilter = SearchParameter;

        var query = CreateQuery();
        if (Result is null || LoadedQuery != QueryKey(query))
        {
            await LoadAsync(query);
        }
    }

    private MediaListQuery CreateQuery()
    {
        return new MediaListQuery
        {
            Search = string.IsNullOrWhiteSpace(SearchParameter) ? null : SearchParameter.Trim(),
            Unused = Unused,
            Page = Math.Max(1, PageParameter ?? 1)
        };
    }

    private async Task LoadAsync(MediaListQuery query)
    {
        _loading = true;
        _loadError = null;
        try
        {
            Result = await MediaService.GetMediaAsync(query);
            LoadedQuery = QueryKey(query);
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

    private Task ReloadAsync()
    {
        return LoadAsync(CreateQuery());
    }

    /// <summary>New uploads appear at the top of the first page, so show that page once a batch is done.</summary>
    private async Task OnUploadsCompletedAsync()
    {
        if (CreateQuery() is { Page: 1, Search: null, Unused: false })
        {
            await ReloadAsync();
        }
        else
        {
            Navigation.NavigateTo(ListUri(clearFilters: true));
        }
    }

    private static string QueryKey(MediaListQuery query)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{query.Search}|{query.Unused}|{query.Page}|{query.PageSize}");
    }

    /// <summary>The library URL with some filters changed; any filter change goes back to page 1.</summary>
    private string ListUri(string? search = null, bool? unused = null, int? page = null, bool clearFilters = false)
    {
        var newSearch = clearFilters ? null : search ?? SearchParameter;
        var newUnused = !clearFilters && (unused ?? Unused);

        return Navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["search"] = string.IsNullOrWhiteSpace(newSearch) ? null : newSearch.Trim(),
            ["unused"] = newUnused ? true : null,
            ["page"] = page is > 1 ? page : null
        });
    }

    private void ApplyFilters()
    {
        Navigation.NavigateTo(ListUri(search: _searchFilter ?? string.Empty));
    }

    private void ToggleUnused(ChangeEventArgs e)
    {
        Navigation.NavigateTo(ListUri(search: _searchFilter ?? string.Empty, unused: e.Value is true));
    }

    private void ClearFilters()
    {
        Navigation.NavigateTo(ListUri(clearFilters: true));
    }

    /// <summary>Edits alt text and caption in a dialog and updates the card in place.</summary>
    private async Task EditMetadataAsync(MediaItemDto item)
    {
        if (await _metadataDialog.EditAsync(item) is { } saved)
        {
            item.AltText = saved.AltText;
            item.Caption = saved.Caption;
        }
    }

    /// <summary>Deletes after confirming (warning when posts use the image), then refreshes the page.</summary>
    private async Task DeleteAsync(MediaItemDto item)
    {
        _busyItemId = item.Id;
        try
        {
            if (await MediaDeletion.ConfirmAndDeleteAsync(_confirm, MediaService, Toasts, item))
            {
                await ReloadAsync();
            }
        }
        finally
        {
            _busyItemId = null;
        }
    }
}
