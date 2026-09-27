using BlogEngine.Client.Components;

using Bunit;

using Microsoft.AspNetCore.Components.Web;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Component tests for <see cref="ConfirmDialog"/>: the awaited result matches the button the admin chose.
/// </summary>
public class ConfirmDialogTests
{
    /// <summary>The confirm button completes the request with <see langword="true"/> and closes the dialog.</summary>
    [Test]
    public async Task Confirm_ReturnsTrue()
    {
        await using var context = CreateContext();
        var cut = context.Render<ConfirmDialog>();

        Task<bool> pending = null!;
        // A statement lambda, so InvokeAsync doesn't wait for the dialog to be answered.
        await cut.InvokeAsync(() => { pending = cut.Instance.ConfirmAsync("Delete?", "Gone for good.", "Delete", "btn-danger"); });
        await cut.Find(".modal-footer .btn-danger").ClickAsync(new MouseEventArgs());

        await Assert.That(await pending).IsTrue();
        await Assert.That(cut.FindAll(".modal")).IsEmpty();
    }

    /// <summary>Escape cancels.</summary>
    [Test]
    public async Task Escape_Cancels()
    {
        await using var context = CreateContext();
        var cut = context.Render<ConfirmDialog>();

        Task<DialogChoice> pending = null!;
        await cut.InvokeAsync(() => { pending = cut.Instance.ShowAsync(new ConfirmOptions("Leave?", "Unsaved changes.")); });
        await cut.Find(".modal").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await Assert.That(await pending).IsEqualTo(DialogChoice.Cancel);
    }

    /// <summary>The optional third button reports <see cref="DialogChoice.Alternate"/>.</summary>
    [Test]
    public async Task AlternateButton_ReturnsAlternate()
    {
        await using var context = CreateContext();
        var cut = context.Render<ConfirmDialog>();

        Task<DialogChoice> pending = null!;
        await cut.InvokeAsync(() =>
        {
            pending = cut.Instance.ShowAsync(new ConfirmOptions("Conflict", "Changed elsewhere.")
            {
                ConfirmText = "Overwrite",
                AlternateText = "Reload"
            });
        });
        await cut.Find(".modal-footer .btn-outline-primary").ClickAsync(new MouseEventArgs());

        await Assert.That(await pending).IsEqualTo(DialogChoice.Alternate);
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}