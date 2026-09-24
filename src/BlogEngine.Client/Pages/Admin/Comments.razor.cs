using System.Globalization;

using BlogEngine.Client.Components;
using BlogEngine.Client.Services;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;

using FluentValidation;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The moderation queue at <c>/admin/comments</c> (design 7.3, 8.4, T3.8), plus the blocklist tab (T3.9).
/// </summary>
/// <remarks>
/// <para>
/// Tabs: Pending (the default), Approved, Spam, Rejected and Blocklist, chosen with <c>?tab=</c>, so a tab can be
/// bookmarked. Each row offers the actions that make sense for its status; selected rows can be moderated in bulk, and
/// the Spam tab can purge old spam. Destructive actions (delete, block, empty spam) ask for confirmation first.
/// </para>
/// <para>
/// The page and the tab counts loaded while prerendering are carried into WebAssembly with
/// <see cref="PersistentStateAttribute"/>. After every action the page and counts are reloaded and the pending count is
/// reported to <see cref="CommentCountNotifier"/>, which keeps the nav badge current.
/// </para>
/// </remarks>
public partial class Comments : ComponentBase
{
    /// <summary>The spam score from which a comment counts as spam, for the score badge.</summary>
    protected const int SpamThreshold = CommentSpam.Threshold;

    private static readonly IReadOnlyList<(CommentsTab Tab, string Label)> Tabs =
    [
        (CommentsTab.Pending, "Pending"),
        (CommentsTab.Approved, "Approved"),
        (CommentsTab.Spam, "Spam"),
        (CommentsTab.Rejected, "Rejected"),
        (CommentsTab.Blocklist, "Blocklist")
    ];

    private static readonly IReadOnlyList<BulkAction> BulkActions =
    [
        new(CommentModerationAction.Approve, "Approve", "bi-check-lg", "btn-outline-success"),
        new(CommentModerationAction.Reject, "Reject", "bi-x-lg", "btn-outline-secondary"),
        new(CommentModerationAction.Spam, "Spam", "bi-exclamation-octagon", "btn-outline-warning"),
        new(CommentModerationAction.Delete, "Delete", "bi-trash", "btn-outline-danger")
    ];

    [Inject] private ICommentModerationService CommentService { get; set; } = default!;
    [Inject] private CommentCountNotifier CountNotifier { get; set; } = default!;
    [Inject] private IToastService Toasts { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;

    /// <summary>The tab from the query string (<c>pending</c>, <c>approved</c>, <c>spam</c>, <c>rejected</c>, <c>blocklist</c>).</summary>
    [SupplyParameterFromQuery(Name = "tab")]
    public string? TabParameter { get; set; }

    /// <summary>1-based page number.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    public int? PageParameter { get; set; }

    /// <summary>The loaded page of comments.</summary>
    [PersistentState]
    public PagedResult<CommentDto>? Result { get; set; }

    /// <summary>The query <see cref="Result"/> was loaded for.</summary>
    [PersistentState]
    public string? LoadedQuery { get; set; }

    /// <summary>How many comments each tab holds.</summary>
    [PersistentState]
    public CommentStatusCounts? Counts { get; set; }

    private readonly HashSet<int> _selected = [];
    private ConfirmDialog _confirm = default!;
    private bool _busy;
    private string? _loadError;

    private CommentsTab Tab => Enum.TryParse<CommentsTab>(TabParameter, ignoreCase: true, out var tab) && Enum.IsDefined(tab)
        ? tab
        : CommentsTab.Pending;

    private bool AllSelected => Result is { Items.Count: > 0 } result && result.Items.All(c => _selected.Contains(c.Id));

    private string EmptyMessage => Tab switch
    {
        CommentsTab.Pending => "Nothing is waiting for moderation.",
        CommentsTab.Approved => "No approved comments yet.",
        CommentsTab.Spam => "No spam. Nice.",
        _ => "No rejected comments."
    };

    /// <summary>Loads the tab's comments and the counts unless the loaded (or restored) ones already match the URL.</summary>
    protected override async Task OnParametersSetAsync()
    {
        if (Counts is null)
        {
            await LoadCountsAsync();
        }

        if (Tab == CommentsTab.Blocklist)
        {
            return;
        }

        var query = CreateQuery();
        if (Result is null || LoadedQuery != QueryKey(query))
        {
            await LoadAsync(query);
        }
    }

    private CommentListQuery CreateQuery()
    {
        return new CommentListQuery
        {
            Status = Tab switch
            {
                CommentsTab.Approved => CommentStatus.Approved,
                CommentsTab.Spam => CommentStatus.Spam,
                CommentsTab.Rejected => CommentStatus.Rejected,
                _ => CommentStatus.Pending
            },
            Page = Math.Max(1, PageParameter ?? 1)
        };
    }

    private async Task LoadAsync(CommentListQuery query)
    {
        _loadError = null;
        try
        {
            Result = await CommentService.GetCommentsAsync(query);
            LoadedQuery = QueryKey(query);
            _selected.IntersectWith(Result.Items.Select(c => c.Id));
        }
        catch (HttpRequestException)
        {
            _loadError = "The comments couldn't be loaded. Check your connection and try again.";
        }
    }

    private async Task LoadCountsAsync()
    {
        try
        {
            Counts = await CommentService.GetCountsAsync();
            CountNotifier.ReportPending(Counts.Pending);
        }
        catch (HttpRequestException)
        {
            // The tabs work without their counts.
        }
    }

    /// <summary>Reloads the current page and the counts after a change.</summary>
    private async Task ReloadAsync()
    {
        await LoadCountsAsync();
        await LoadAsync(CreateQuery());
    }

    private static string QueryKey(CommentListQuery query)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{query.Status}|{query.Page}|{query.PageSize}");
    }

    private int? CountOf(CommentsTab tab)
    {
        return Counts is null
            ? null
            : tab switch
            {
                CommentsTab.Pending => Counts.Pending,
                CommentsTab.Approved => Counts.Approved,
                CommentsTab.Spam => Counts.Spam,
                CommentsTab.Rejected => Counts.Rejected,
                _ => null
            };
    }

    /// <summary>The URL of a tab (and page); the pending tab's first page is plain <c>/admin/comments</c>.</summary>
    private string TabUri(CommentsTab tab, int page = 1)
    {
        return Navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["tab"] = tab == CommentsTab.Pending ? null : tab.ToString().ToLowerInvariant(),
            ["page"] = page > 1 ? page : null
        });
    }

    private string Relative(DateTimeOffset? value)
    {
        return value is { } date ? RelativeTime.Format(date, TimeProvider.GetUtcNow()) : string.Empty;
    }

    private void ToggleSelected(int id, ChangeEventArgs e)
    {
        if (e.Value is true)
        {
            _selected.Add(id);
        }
        else
        {
            _selected.Remove(id);
        }
    }

    private void ToggleAll(ChangeEventArgs e)
    {
        _selected.Clear();
        if (e.Value is true && Result is not null)
        {
            _selected.UnionWith(Result.Items.Select(c => c.Id));
        }
    }

    /// <summary>Approves, rejects, flags or (after confirming) deletes one comment.</summary>
    private async Task ModerateAsync(CommentDto comment, CommentModerationAction action)
    {
        if (action == CommentModerationAction.Delete && !await _confirm.ConfirmAsync(
                "Delete comment?",
                $"The comment by {comment.AuthorName} will be deleted permanently, together with any replies to it.",
                "Delete",
                "btn-danger"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            if (await CommentService.ModerateAsync(comment.Id, action))
            {
                Toasts.ShowSuccess(action switch
                {
                    CommentModerationAction.Approve => $"The comment by {comment.AuthorName} is now on the site.",
                    CommentModerationAction.Reject => $"The comment by {comment.AuthorName} was rejected.",
                    CommentModerationAction.Spam => $"The comment by {comment.AuthorName} was marked as spam.",
                    _ => $"The comment by {comment.AuthorName} was deleted."
                });
            }
            else
            {
                Toasts.ShowWarning("That comment no longer exists.");
            }
        });
    }

    /// <summary>Applies an action to every selected comment, confirming deletes first.</summary>
    private async Task BulkAsync(CommentModerationAction action)
    {
        var ids = _selected.ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var noun = ids.Count == 1 ? "comment" : "comments";
        if (action == CommentModerationAction.Delete && !await _confirm.ConfirmAsync(
                $"Delete {ids.Count} {noun}?",
                $"The selected {noun} will be deleted permanently, together with any replies.",
                "Delete",
                "btn-danger"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            try
            {
                var changed = await CommentService.ModerateManyAsync(new CommentBulkRequest { Ids = ids, Action = action });
                _selected.Clear();
                Toasts.ShowSuccess(action switch
                {
                    CommentModerationAction.Approve => $"{changed} {Plural(changed)} approved.",
                    CommentModerationAction.Reject => $"{changed} {Plural(changed)} rejected.",
                    CommentModerationAction.Spam => $"{changed} {Plural(changed)} marked as spam.",
                    _ => $"{changed} {Plural(changed)} deleted."
                });
            }
            catch (ValidationException ex)
            {
                Toasts.ShowWarning(string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)));
            }
        });
    }

    /// <summary>Blocks the commenter (email and IP) after confirming, and sends all their comments to spam.</summary>
    private async Task BlockAsync(CommentDto comment)
    {
        var confirmed = await _confirm.ConfirmAsync(
            "Block commenter?",
            $"Future comments from {comment.AuthorEmail} or the same IP address will go straight to spam, and all of their " +
            "comments will be marked as spam now. You can undo the block on the Blocklist tab.",
            "Block commenter",
            "btn-danger");
        if (!confirmed)
        {
            return;
        }

        await RunAsync(async () =>
        {
            if (await CommentService.BlockCommenterAsync(comment.Id) is { } result)
            {
                Toasts.ShowSuccess($"{comment.AuthorName} is blocked; {result.CommentsMarkedSpam} {Plural(result.CommentsMarkedSpam)} marked as spam.");
            }
            else
            {
                Toasts.ShowWarning("That comment no longer exists.");
            }
        });
    }

    /// <summary>Deletes spam older than 30 days after confirming.</summary>
    private async Task EmptySpamAsync()
    {
        var confirmed = await _confirm.ConfirmAsync(
            "Empty spam?",
            "Spam older than 30 days will be deleted permanently. Newer spam stays, in case something was flagged by mistake.",
            "Empty spam",
            "btn-danger");
        if (!confirmed)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var deleted = await CommentService.EmptySpamAsync();
            Toasts.ShowSuccess(deleted == 0 ? "There was no spam older than 30 days." : $"{deleted} spam {Plural(deleted)} deleted.");
        });
    }

    /// <summary>Runs an action with the buttons disabled, then refreshes the page and counts whatever the outcome.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
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
            _busy = false;
        }

        await ReloadAsync();
    }

    private static string Plural(int count)
    {
        return count == 1 ? "comment" : "comments";
    }

    /// <summary>A bulk action button.</summary>
    private sealed record BulkAction(CommentModerationAction Action, string Label, string Icon, string ButtonClass);
}

/// <summary>The tabs of the comments page.</summary>
public enum CommentsTab
{
    /// <summary>Awaiting moderation (the default).</summary>
    Pending,

    /// <summary>On the site.</summary>
    Approved,

    /// <summary>Flagged as spam.</summary>
    Spam,

    /// <summary>Rejected by the moderator.</summary>
    Rejected,

    /// <summary>Blocked commenters, keywords and domains.</summary>
    Blocklist
}
