using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlogEngine.Client.Components;

/// <summary>
/// A Bootstrap modal that asks the admin to confirm an action (design 5.2, T2.8), rendered entirely by Blazor
/// so it needs no Bootstrap JavaScript. Place one instance on a page and await <see cref="ShowAsync"/> or
/// <see cref="ConfirmAsync"/> wherever a confirmation is needed.
/// </summary>
/// <example>
/// <code>
/// &lt;ConfirmDialog @ref="_confirm" /&gt;
///
/// if (await _confirm.ConfirmAsync("Delete post?", "The post moves to the trash.", "Delete", "btn-danger"))
/// {
///     await PostService.DeleteAsync(id);
/// }
/// </code>
/// </example>
public partial class ConfirmDialog : ComponentBase
{
    private readonly string _titleId = $"confirm-title-{Guid.NewGuid():N}";
    private ConfirmOptions? _options;
    private TaskCompletionSource<DialogChoice>? _pending;
    private ElementReference _confirmButton;
    private bool _focusPending;

    /// <summary>Whether the dialog is currently open.</summary>
    public bool IsOpen => _options is not null;

    /// <summary>
    /// Opens the dialog and completes when the admin chooses. Opening it again while it is open cancels the
    /// earlier request, so a caller never waits forever.
    /// </summary>
    public Task<DialogChoice> ShowAsync(ConfirmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _pending?.TrySetResult(DialogChoice.Cancel);
        _pending = new TaskCompletionSource<DialogChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _options = options;
        _focusPending = true;
        StateHasChanged();

        return _pending.Task;
    }

    /// <summary>Shows a two-button dialog; <see langword="true"/> when the admin confirms.</summary>
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText,
        string confirmButtonClass = "btn-primary")
    {
        var choice = await ShowAsync(new ConfirmOptions(title, message)
        {
            ConfirmText = confirmText,
            ConfirmButtonClass = confirmButtonClass
        });

        return choice == DialogChoice.Confirm;
    }

    /// <summary>Moves focus into the dialog when it opens, so the keyboard and screen readers land on it.</summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusPending && _options is not null)
        {
            _focusPending = false;
            await _confirmButton.FocusAsync();
        }
    }

    /// <summary>Escape cancels, like a Bootstrap modal.</summary>
    private Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        return e.Key == "Escape" ? CloseAsync(DialogChoice.Cancel) : Task.CompletedTask;
    }

    /// <summary>Closes the dialog and reports the choice to the waiting caller.</summary>
    private Task CloseAsync(DialogChoice choice)
    {
        _options = null;
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(choice);

        return Task.CompletedTask;
    }
}

/// <summary>What <see cref="ConfirmDialog"/> shows.</summary>
/// <param name="Title">Heading of the dialog.</param>
/// <param name="Message">Explanation of what will happen.</param>
public sealed record ConfirmOptions(string Title, string Message)
{
    /// <summary>Label of the primary button.</summary>
    public string ConfirmText { get; init; } = "OK";

    /// <summary>Bootstrap button class of the primary button, such as <c>btn-danger</c> for destructive actions.</summary>
    public string ConfirmButtonClass { get; init; } = "btn-primary";

    /// <summary>Label of an optional third button; <see langword="null"/> hides it.</summary>
    public string? AlternateText { get; init; }

    /// <summary>Label of the button that dismisses the dialog.</summary>
    public string CancelText { get; init; } = "Cancel";
}

/// <summary>The button the admin chose in a <see cref="ConfirmDialog"/>.</summary>
public enum DialogChoice
{
    /// <summary>Dismissed with Cancel, the close button or Escape.</summary>
    Cancel,

    /// <summary>The primary button.</summary>
    Confirm,

    /// <summary>The optional third button.</summary>
    Alternate
}
