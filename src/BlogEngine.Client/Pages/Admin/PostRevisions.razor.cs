using System.Globalization;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// A post's revision history at <c>/admin/posts/{id}/revisions</c> (design 7.3, A13, T4.3): the saved, published and
/// autosaved versions, newest first, each compared side by side with the current post, and a Restore button that
/// loads a revision into the editor.
/// </summary>
/// <remarks>
/// <para>
/// The selected revision is in the query string (<c>?revision=12</c>), so a comparison can be linked and survives a
/// reload. The post, the list and the selected revision are loaded while prerendering and carried into WebAssembly with
/// <see cref="PersistentStateAttribute"/>; the diff is computed from them with <see cref="LineDiff"/>.
/// </para>
/// <para>
/// Restoring doesn't save anything by itself: the editor opens with the revision's title and content as unsaved
/// changes (<c>?restore=</c>), and saving (or Update, for a published post) makes them the post's content again. The
/// comparison is with the saved post, so changes still open in an editor or staged by autosave aren't included.
/// </para>
/// </remarks>
public partial class PostRevisions : ComponentBase
{
    /// <summary>Unchanged lines shown around each change while "show all lines" is off.</summary>
    private const int ContextLines = 3;

    [Inject] private IPostAdminService Posts { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;

    /// <summary>The post id from the route.</summary>
    [Parameter] public int Id { get; set; }

    /// <summary>The revision to compare, from the query string; the newest one when absent.</summary>
    [SupplyParameterFromQuery(Name = "revision")]
    public int? RevisionParameter { get; set; }

    /// <summary>The post as saved, which the revisions are compared with.</summary>
    [PersistentState]
    public PostEditDto? Post { get; set; }

    /// <summary>The revisions, newest first.</summary>
    [PersistentState]
    public List<PostRevisionSummaryDto>? Revisions { get; set; }

    /// <summary>The revision being compared, with its content.</summary>
    [PersistentState]
    public PostRevisionDto? Selected { get; set; }

    /// <summary>Whether the post doesn't exist (or is in the trash).</summary>
    [PersistentState]
    public bool PostMissing { get; set; }

    private IReadOnlyList<DiffRow> _diff = [];
    private IReadOnlyList<DisplayRow> _rows = [];
    private int _diffSourceRevisionId;
    private bool _showAll;
    private bool _loading;
    private string? _loadError;

    /// <summary>Changed rows in the comparison.</summary>
    private int ChangeCount => _diff.Count(r => r.Kind != DiffRowKind.Unchanged);

    private bool TitleChanged => Selected is not null && Post is not null && Selected.Title != Post.Title;

    /// <summary>Loads the post and its revisions once, then the selected revision whenever the query string changes.</summary>
    protected override async Task OnParametersSetAsync()
    {
        _loadError = null;
        try
        {
            if (Post?.Id != Id && !PostMissing)
            {
                Post = await Posts.GetPostAsync(Id);
                PostMissing = Post is null;
                Revisions = Post is null ? null : [.. await Posts.GetRevisionsAsync(Id) ?? []];
            }

            var wanted = RevisionParameter ?? Revisions?.FirstOrDefault()?.Id;
            if (wanted is { } revisionId && Selected?.Id != revisionId)
            {
                _loading = true;
                Selected = await Posts.GetRevisionAsync(Id, revisionId);
            }
        }
        catch (HttpRequestException)
        {
            _loadError = "The revisions couldn't be loaded. Check your connection and reload the page.";
        }
        finally
        {
            _loading = false;
        }

        UpdateDiff();
    }

    /// <summary>Recomputes the comparison when the selected revision changed.</summary>
    private void UpdateDiff()
    {
        if (Selected is null || Post is null)
        {
            _diff = [];
            _rows = [];
            _diffSourceRevisionId = 0;
            return;
        }

        if (_diffSourceRevisionId != Selected.Id)
        {
            _diff = LineDiff.Compare(Selected.ContentMarkdown, Post.ContentMarkdown);
            _diffSourceRevisionId = Selected.Id;
        }

        _rows = BuildRows(_diff, _showAll);
    }

    private void ToggleShowAll()
    {
        _showAll = !_showAll;
        _rows = BuildRows(_diff, _showAll);
    }

    /// <summary>
    /// The rows to show: every row, or only changes with <see cref="ContextLines"/> unchanged lines around them and the
    /// rest collapsed into "N unchanged lines" markers.
    /// </summary>
    private static List<DisplayRow> BuildRows(IReadOnlyList<DiffRow> diff, bool showAll)
    {
        if (showAll)
        {
            return [.. diff.Select(r => new DisplayRow(r, 0))];
        }

        var keep = new bool[diff.Count];
        for (var i = 0; i < diff.Count; i++)
        {
            if (diff[i].Kind == DiffRowKind.Unchanged)
            {
                continue;
            }

            for (var k = Math.Max(0, i - ContextLines); k <= Math.Min(diff.Count - 1, i + ContextLines); k++)
            {
                keep[k] = true;
            }
        }

        var rows = new List<DisplayRow>();
        var skipped = 0;
        for (var i = 0; i < diff.Count; i++)
        {
            if (keep[i])
            {
                if (skipped > 0)
                {
                    rows.Add(new DisplayRow(null, skipped));
                    skipped = 0;
                }

                rows.Add(new DisplayRow(diff[i], 0));
            }
            else
            {
                skipped++;
            }
        }

        if (skipped > 0)
        {
            rows.Add(new DisplayRow(null, skipped));
        }

        return rows;
    }

    /// <summary>The page URL comparing another revision.</summary>
    private string RevisionUri(int revisionId)
    {
        return Navigation.GetUriWithQueryParameter("revision", revisionId);
    }

    /// <summary>Opens the editor with the selected revision loaded as unsaved changes.</summary>
    private void Restore()
    {
        if (Selected is not null)
        {
            Navigation.NavigateTo(string.Create(CultureInfo.InvariantCulture, $"admin/posts/{Id}?restore={Selected.Id}"));
        }
    }

    /// <summary>A revision's timestamp in the author's local time, such as "Sep 23, 10:42 AM".</summary>
    private static string TimeText(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        return string.Create(CultureInfo.CurrentCulture, $"{local:MMM d, yyyy} {local:t}");
    }

    private string Relative(DateTimeOffset value)
    {
        return RelativeTime.Format(value, TimeProvider.GetUtcNow());
    }

    /// <summary>The badge label and color for a revision kind.</summary>
    private static (string Label, string CssClass) KindBadge(RevisionKind kind) => kind switch
    {
        RevisionKind.Publish => ("Published", "text-bg-success"),
        RevisionKind.Manual => ("Saved", "text-bg-primary"),
        _ => ("Autosave", "text-bg-secondary")
    };

    /// <summary>Background classes for the old and new halves of a row.</summary>
    private static (string Old, string New) RowClasses(DiffRowKind kind) => kind switch
    {
        DiffRowKind.Removed => ("diff-removed", "diff-empty"),
        DiffRowKind.Added => ("diff-empty", "diff-added"),
        DiffRowKind.Changed => ("diff-removed", "diff-added"),
        _ => (string.Empty, string.Empty)
    };

    /// <summary>A row of the comparison table: a diff row, or a marker for <see cref="Skipped"/> hidden unchanged lines.</summary>
    private sealed record DisplayRow(DiffRow? Row, int Skipped);
}