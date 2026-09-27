namespace BlogEngine.Client.Services;

/// <summary>
/// Tells the admin nav badge how many comments are pending whenever a page learns a newer count (design 8.4, T3.10),
/// so moderating on <c>/admin/comments</c> or loading the dashboard updates the badge without a reload.
/// </summary>
/// <remarks>
/// Registered as scoped. In WebAssembly a scope lives as long as the app, so the badge in the layout and the page
/// share one instance, just like <see cref="ToastService"/> and its container.
/// </remarks>
public sealed class CommentCountNotifier
{
    /// <summary>The latest known pending count, or <see langword="null"/> before anything reported one.</summary>
    public int? PendingCount { get; private set; }

    /// <summary>Raised with the new count when it changes.</summary>
    public event Action<int>? PendingCountChanged;

    /// <summary>Records a freshly loaded pending count and notifies listeners if it changed.</summary>
    public void ReportPending(int count)
    {
        if (PendingCount == count)
        {
            return;
        }

        PendingCount = count;
        PendingCountChanged?.Invoke(count);
    }
}