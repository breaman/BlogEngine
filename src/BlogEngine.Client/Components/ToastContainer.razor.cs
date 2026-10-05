using BlogEngine.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace BlogEngine.Client.Components;

public partial class ToastContainer : ComponentBase, IDisposable
{
    [Inject] private IToastService ToastService { get; set; } = default!;

    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(5);

    private readonly List<ToastMessage> _toasts = [];

    // Cancelled on dispose so pending dismiss timers stop instead of touching a disposed component.
    private readonly CancellationTokenSource _disposed = new();

    /// <summary>Subscribes to the toast service so new toasts are rendered as they are requested.</summary>
    protected override void OnInitialized()
    {
        ToastService.OnToastAdded += HandleToastAdded;
    }

    /// <summary>
    /// Shows the toast and takes it away again after <see cref="VisibleFor"/>. A toast is shown by
    /// rendering it with Bootstrap's <c>show</c> class and dismissed by removing it from the list,
    /// so the component needs no JavaScript interop.
    /// </summary>
    private void HandleToastAdded(ToastMessage toast)
    {
        _ = InvokeAsync(async () =>
        {
            _toasts.Add(toast);
            StateHasChanged();

            try
            {
                await Task.Delay(VisibleFor, _disposed.Token);
            }
            catch (OperationCanceledException)
            {
                // The component was disposed while the toast was showing; nothing is left to update.
                return;
            }

            RemoveToast(toast.Id);
            StateHasChanged();
        });
    }

    /// <summary>Removes the toast with the given id, if it is still showing.</summary>
    private void RemoveToast(Guid id)
    {
        var toast = _toasts.FirstOrDefault(t => t.Id == id);
        if (toast is not null)
        {
            _toasts.Remove(toast);
        }
    }

    /// <summary>Maps a toast type to its Bootstrap contextual color name.</summary>
    private static string GetBootstrapClass(ToastType type) => type switch
    {
        ToastType.Success => "success",
        ToastType.Error => "danger",
        ToastType.Warning => "warning",
        ToastType.Info => "info",
        _ => "secondary"
    };

    /// <summary>Unsubscribes from the toast service and cancels any pending dismiss timers.</summary>
    public void Dispose()
    {
        ToastService.OnToastAdded -= HandleToastAdded;
        _disposed.Cancel();
        _disposed.Dispose();
    }
}
