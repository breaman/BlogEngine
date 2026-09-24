using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;
using BlogEngine.Shared.Text;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The admin landing page at <c>/admin</c> (design 7.3, O1, T3.10, T4.1): counts of drafts, scheduled and published posts
/// and of comments awaiting moderation, the next scheduled posts, and the latest posts and comments.
/// </summary>
/// <remarks>
/// The summary loaded while prerendering is carried into WebAssembly with <see cref="PersistentStateAttribute"/>. The
/// pending count is also reported to <see cref="CommentCountNotifier"/>, so the nav badge agrees with the card.
/// </remarks>
public partial class Dashboard : ComponentBase
{
    /// <summary>The summary cards shown across the top of the dashboard.</summary>
    private static readonly IReadOnlyList<DashboardCard> Cards =
    [
        new("Drafts", "bi-pencil-square", "admin/posts?status=Draft", s => s.DraftCount),
        new("Scheduled", "bi-calendar-event", "admin/posts?status=Scheduled", s => s.ScheduledCount),
        new("Published", "bi-journal-check", "admin/posts?status=Published", s => s.PublishedCount),
        new("Pending comments", "bi-chat-dots", "admin/comments", s => s.PendingCommentCount, Highlight: true)
    ];

    [Inject] private IDashboardService DashboardService { get; set; } = default!;
    [Inject] private CommentCountNotifier CountNotifier { get; set; } = default!;
    [Inject] private TimeProvider TimeProvider { get; set; } = default!;

    /// <summary>The counts and recent activity.</summary>
    [PersistentState]
    public DashboardSummaryDto? Summary { get; set; }

    private string? _loadError;

    /// <summary>Loads the summary unless it was restored from prerendering, and updates the nav badge.</summary>
    protected override async Task OnInitializedAsync()
    {
        if (Summary is null)
        {
            await LoadAsync();
        }
        else
        {
            CountNotifier.ReportPending(Summary.PendingCommentCount);
        }
    }

    private async Task LoadAsync()
    {
        _loadError = null;
        try
        {
            Summary = await DashboardService.GetSummaryAsync();
            CountNotifier.ReportPending(Summary.PendingCommentCount);
        }
        catch (HttpRequestException)
        {
            _loadError = "The dashboard couldn't be loaded. Check your connection and try again.";
        }
    }

    private string Relative(DateTimeOffset? value)
    {
        return value is { } date ? RelativeTime.Format(date, TimeProvider.GetUtcNow()) : string.Empty;
    }

    /// <summary>When a scheduled post goes live, in the author's local time, such as "Oct 1, 9:00 AM".</summary>
    private static string Scheduled(DateTimeOffset? value)
    {
        if (value is not { } date)
        {
            return string.Empty;
        }

        var local = date.ToLocalTime();
        return string.Create(CultureInfo.CurrentCulture, $"{local:MMM d}, {local:t}");
    }

    /// <summary>The start of a comment as plain text, for a one-line preview.</summary>
    private static string Excerpt(string html)
    {
        // Block boundaries become spaces; inline tags vanish, so "<strong>thanks</strong>!" stays "thanks!".
        var text = Regex.Replace(html, @"</(?:p|li|blockquote|pre)>|<br\s*/?>", " ", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", string.Empty));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= 140 ? text : text[..140] + "…";
    }

    private static string StatusBadge(CommentStatus status)
    {
        return status switch
        {
            CommentStatus.Pending => "text-bg-warning",
            CommentStatus.Approved => "text-bg-success",
            CommentStatus.Spam => "text-bg-danger",
            _ => "text-bg-secondary"
        };
    }

    /// <summary>A summary card: its title, Bootstrap Icons class, link and the number it shows.</summary>
    private sealed record DashboardCard(string Title, string Icon, string Href, Func<DashboardSummaryDto, int> Count, bool Highlight = false);
}
