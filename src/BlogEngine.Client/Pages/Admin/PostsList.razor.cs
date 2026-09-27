using System.Globalization;

using BlogEngine.Client.Components;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The admin posts list at <c>/admin/posts</c> (design 7.3, O2, T1.14, T4.1, T4.23): status tabs (including Scheduled and
/// Trash), tag filter, text search, paging and row actions (edit, view, unpublish or unschedule, move to trash; in the
/// trash: restore and delete permanently, plus "Empty trash").
/// </summary>
/// <remarks>
/// <para>
/// The filters live in the query string (<c>?status=Draft&amp;tag=csharp&amp;search=blazor&amp;page=2</c>), so a filtered
/// list can be bookmarked, reloaded and navigated with the back button. Changing a filter navigates, and
/// <see cref="OnParametersSetAsync"/> reloads when the query no longer matches the loaded page.
/// </para>
/// <para>
/// The page loaded while prerendering is carried into WebAssembly with <see cref="PersistentStateAttribute"/>,
/// tagged with the query it was loaded for (<see cref="LoadedQuery"/>), so hydration doesn't fetch it again.
/// </para>
/// </remarks>
public partial class PostsList : ComponentBase
{
    /// <summary>The status tabs, with the trash last (design 7.3).</summary>
    private static readonly IReadOnlyList<(PostListStatus Status, string Label)> StatusTabs =
    [
        (PostListStatus.All, "All"),
        (PostListStatus.Draft, "Drafts"),
        (PostListStatus.Scheduled, "Scheduled"),
        (PostListStatus.Published, "Published"),
        (PostListStatus.Trash, "Trash")
    ];

    [Inject] private IPostAdminService PostService { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;

    /// <summary>Status tab from the query string (<c>All</c>, <c>Draft</c>, <c>Scheduled</c>, <c>Published</c> or <c>Trash</c>).</summary>
    [SupplyParameterFromQuery(Name = "status")]
    public string? StatusParameter { get; set; }

    /// <summary>Tag name or slug to filter by.</summary>
    [SupplyParameterFromQuery(Name = "tag")]
    public string? TagParameter { get; set; }

    /// <summary>Text to find in titles, slugs and summaries.</summary>
    [SupplyParameterFromQuery(Name = "search")]
    public string? SearchParameter { get; set; }

    /// <summary>1-based page number.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    public int? PageParameter { get; set; }

    /// <summary>The loaded page of posts.</summary>
    [PersistentState]
    public PagedResult<PostSummaryDto>? Result { get; set; }

    /// <summary>The query <see cref="Result"/> was loaded for, in the form produced by <see cref="QueryKey"/>.</summary>
    [PersistentState]
    public string? LoadedQuery { get; set; }

    private ConfirmDialog _confirm = default!;
    private string? _tagFilter;
    private string? _searchFilter;
    private bool _loading;
    private string? _loadError;
    private int? _busyPostId;
    private bool _emptyingTrash;

    private PostListStatus Status => Enum.TryParse<PostListStatus>(StatusParameter, ignoreCase: true, out var status)
        && Enum.IsDefined(status) ? status : PostListStatus.All;

    private int CurrentPage => Math.Max(1, PageParameter ?? 1);

    private bool IsTrash => Status == PostListStatus.Trash;

    private bool HasFilters => !string.IsNullOrWhiteSpace(TagParameter) || !string.IsNullOrWhiteSpace(SearchParameter);

    /// <summary>Loads the page for the current query unless the loaded (or restored) page already matches it.</summary>
    protected override async Task OnParametersSetAsync()
    {
        _tagFilter = TagParameter;
        _searchFilter = SearchParameter;

        var query = CreateQuery();
        if (Result is null || LoadedQuery != QueryKey(query))
        {
            await LoadAsync(query);
        }
    }

    private PostListQuery CreateQuery()
    {
        return new PostListQuery
        {
            Status = Status,
            Tag = string.IsNullOrWhiteSpace(TagParameter) ? null : TagParameter.Trim(),
            Search = string.IsNullOrWhiteSpace(SearchParameter) ? null : SearchParameter.Trim(),
            Page = CurrentPage
        };
    }

    private async Task LoadAsync(PostListQuery query)
    {
        _loading = true;
        _loadError = null;
        try
        {
            Result = await PostService.GetPostsAsync(query);
            LoadedQuery = QueryKey(query);
        }
        catch (HttpRequestException)
        {
            _loadError = "The posts couldn't be loaded. Check your connection and try again.";
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

    /// <summary>Identifies a query, so a restored or loaded page can be matched to the current URL.</summary>
    private static string QueryKey(PostListQuery query)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{query.Status}|{query.Tag}|{query.Search}|{query.Page}|{query.PageSize}");
    }

    /// <summary>The list URL with some filters changed; any filter change goes back to page 1.</summary>
    private string ListUri(PostListStatus? status = null, string? tag = null, string? search = null, int? page = null,
        bool clearFilters = false)
    {
        var newStatus = status ?? Status;
        var newTag = clearFilters ? null : tag ?? TagParameter;
        var newSearch = clearFilters ? null : search ?? SearchParameter;

        return Navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["status"] = newStatus == PostListStatus.All ? null : newStatus.ToString(),
            ["tag"] = string.IsNullOrWhiteSpace(newTag) ? null : newTag.Trim(),
            ["search"] = string.IsNullOrWhiteSpace(newSearch) ? null : newSearch.Trim(),
            ["page"] = page is > 1 ? page : null
        });
    }

    private void ApplyFilters()
    {
        Navigation.NavigateTo(ListUri(tag: _tagFilter ?? string.Empty, search: _searchFilter ?? string.Empty));
    }

    private void FilterByTag(string tag)
    {
        Navigation.NavigateTo(ListUri(tag: tag));
    }

    private void ClearFilters()
    {
        Navigation.NavigateTo(ListUri(clearFilters: true));
    }

    /// <summary>
    /// Returns a published post to draft after confirming; its public URL stops working. For a scheduled post this is
    /// "Unschedule", which also forgets the scheduled date.
    /// </summary>
    private async Task UnpublishAsync(PostSummaryDto post)
    {
        var scheduled = IsScheduled(post);
        var confirmed = scheduled
            ? await _confirm.ConfirmAsync(
                "Unschedule post?",
                $"\"{post.Title}\" goes back to being a draft and won't be published at the scheduled time.",
                "Unschedule",
                "btn-warning")
            : await _confirm.ConfirmAsync(
                "Unpublish post?",
                $"\"{post.Title}\" goes back to being a draft and its public URL stops working until you publish it again.",
                "Unpublish",
                "btn-warning");
        if (!confirmed)
        {
            return;
        }

        await RunRowActionAsync(post, async () =>
        {
            (bool Success, string Text) message = await PostService.UnpublishAsync(post.Id, new UnpublishPostRequest { RowVersion = post.RowVersion }) switch
            {
                PostSaved => (true, $"\"{post.Title}\" is now a draft."),
                PostConflict => (false, $"\"{post.Title}\" was changed elsewhere, so it wasn't unpublished. The list has been refreshed."),
                PostNotFound => (false, $"\"{post.Title}\" no longer exists."),
                _ => (false, $"\"{post.Title}\" couldn't be unpublished.")
            };

            ShowResult(message);
        });
    }

    /// <summary>Moves a post to the trash after confirming.</summary>
    private async Task DeleteAsync(PostSummaryDto post)
    {
        var confirmed = await _confirm.ConfirmAsync(
            "Move to trash?",
            $"\"{post.Title}\" will be moved to the trash and removed from the site.",
            "Move to trash",
            "btn-danger");
        if (!confirmed)
        {
            return;
        }

        await RunRowActionAsync(post, async () =>
        {
            var deleted = await PostService.DeleteAsync(post.Id);
            ShowResult(deleted
                ? (true, $"\"{post.Title}\" was moved to the trash.")
                : (false, $"\"{post.Title}\" no longer exists."));
        });
    }

    /// <summary>Takes a post out of the trash as a draft (design 6.8).</summary>
    private async Task RestoreAsync(PostSummaryDto post)
    {
        await RunRowActionAsync(post, async () =>
        {
            ShowResult(await PostService.RestoreAsync(post.Id) switch
            {
                PostSaved => (true, $"\"{post.Title}\" was restored as a draft."),
                _ => (false, $"\"{post.Title}\" is no longer in the trash.")
            });
        });
    }

    /// <summary>Deletes one post in the trash for good, after confirming.</summary>
    private async Task DeletePermanentlyAsync(PostSummaryDto post)
    {
        var confirmed = await _confirm.ConfirmAsync(
            "Delete permanently?",
            $"\"{post.Title}\" will be deleted with its comments and revisions. This can't be undone.",
            "Delete permanently",
            "btn-danger");
        if (!confirmed)
        {
            return;
        }

        await RunRowActionAsync(post, async () =>
        {
            var deleted = await PostService.DeletePermanentlyAsync(post.Id);
            ShowResult(deleted
                ? (true, $"\"{post.Title}\" was deleted permanently.")
                : (false, $"\"{post.Title}\" is no longer in the trash."));
        });
    }

    /// <summary>Deletes every post in the trash for good, after confirming (design 6.8, "Empty trash").</summary>
    private async Task EmptyTrashAsync()
    {
        var confirmed = await _confirm.ConfirmAsync(
            "Empty the trash?",
            "Every post in the trash will be deleted with its comments and revisions. This can't be undone.",
            "Empty trash",
            "btn-danger");
        if (!confirmed)
        {
            return;
        }

        _emptyingTrash = true;
        try
        {
            var deleted = await PostService.EmptyTrashAsync();
            Toasts.ShowSuccess(deleted == 1 ? "1 post was deleted permanently." : $"{deleted:N0} posts were deleted permanently.");
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The server couldn't be reached. Check your connection and try again.");
        }
        finally
        {
            _emptyingTrash = false;
        }

        await ReloadAsync();
    }

    /// <summary>Runs a row action with its buttons disabled, then refreshes the list whatever the outcome.</summary>
    private async Task RunRowActionAsync(PostSummaryDto post, Func<Task> action)
    {
        _busyPostId = post.Id;
        try
        {
            await action();
        }
        catch (HttpRequestException)
        {
            Toasts.ShowError("The server couldn't be reached. Check your connection and try again.");
        }
        finally
        {
            _busyPostId = null;
        }

        await ReloadAsync();
    }

    private void ShowResult((bool Success, string Text) result)
    {
        if (result.Success)
        {
            Toasts.ShowSuccess(result.Text);
        }
        else
        {
            Toasts.ShowWarning(result.Text);
        }
    }

    /// <summary>Whether the post is published with a date that is still ahead.</summary>
    private bool IsScheduled(PostSummaryDto post)
    {
        return PostSchedule.IsScheduled(post.Status, post.PublishedOn, TimeProvider.GetUtcNow());
    }

    /// <summary>
    /// When the post was trashed (in the trash), the publish date for published posts, with the time as well for scheduled
    /// ones (when they go live matters), otherwise the last change, in the author's local time.
    /// </summary>
    private string DateText(PostSummaryDto post)
    {
        if (post.DeletedOn is { } deletedOn)
        {
            return deletedOn.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        }

        if (post.Status != PostStatus.Published)
        {
            return post.ModifiedOn?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? "—";
        }

        if (post.PublishedOn is not { } publishedOn)
        {
            return "—";
        }

        var local = publishedOn.ToLocalTime();
        return IsScheduled(post)
            ? string.Create(CultureInfo.CurrentCulture, $"{local:d} {local:t}")
            : local.ToString("d", CultureInfo.CurrentCulture);
    }
}