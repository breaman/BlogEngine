using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Pages.Admin;

/// <summary>
/// The admin landing page at <c>/admin</c> (design 7.3, O1). For now it shows empty summary cards; the
/// counts are filled in once posts and comments exist.
/// </summary>
public partial class Dashboard : ComponentBase
{
    /// <summary>The summary cards shown across the top of the dashboard.</summary>
    private static readonly IReadOnlyList<DashboardCard> Cards =
    [
        new("Drafts", "bi-pencil-square"),
        new("Scheduled", "bi-calendar-event"),
        new("Published", "bi-journal-check"),
        new("Pending comments", "bi-chat-dots")
    ];

    /// <summary>A summary card: its title and Bootstrap Icons class.</summary>
    private sealed record DashboardCard(string Title, string Icon);
}
