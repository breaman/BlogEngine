using BlogEngine.Client.Services;
using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Components;

/// <summary>
/// The pending-comments count next to "Comments" in the admin nav (design 8.4, T3.10). It loads the count once, then
/// follows <see cref="CommentCountNotifier"/>, so moderating or opening the dashboard updates it without a reload.
/// </summary>
/// <remarks>
/// It sits in the static admin layout as its own InteractiveWebAssembly island, like the toast container, and must be
/// wrapped in an <c>AuthorizeView</c> for admins so anonymous visitors of the login redirect never see the count.
/// </remarks>
public partial class PendingCommentsBadge : ComponentBase, IDisposable
{
    [Inject] private ICommentModerationService CommentService { get; set; } = default!;
    [Inject] private CommentCountNotifier Notifier { get; set; } = default!;

    /// <summary>Comments awaiting moderation, carried from prerendering into WebAssembly.</summary>
    [PersistentState]
    public int? PendingCount { get; set; }

    /// <summary>Loads the count unless a page already reported one (or it was restored), then listens for changes.</summary>
    protected override async Task OnInitializedAsync()
    {
        Notifier.PendingCountChanged += OnPendingCountChanged;

        PendingCount ??= Notifier.PendingCount;
        if (PendingCount is null)
        {
            try
            {
                PendingCount = (await CommentService.GetCountsAsync()).Pending;
            }
            catch (HttpRequestException)
            {
                // The badge is a convenience; the comments page shows the real queue.
                return;
            }
        }

        Notifier.ReportPending(PendingCount.Value);
    }

    /// <summary>Stops listening when the layout goes away.</summary>
    public void Dispose()
    {
        Notifier.PendingCountChanged -= OnPendingCountChanged;
        GC.SuppressFinalize(this);
    }

    private void OnPendingCountChanged(int count)
    {
        PendingCount = count;
        _ = InvokeAsync(StateHasChanged);
    }
}