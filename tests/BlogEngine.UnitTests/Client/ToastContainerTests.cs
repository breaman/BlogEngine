using BlogEngine.Client.Components;
using BlogEngine.Client.Services;
using BlogEngine.Shared.Services;

using Bunit;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Component tests for <see cref="ToastContainer"/>: toasts are shown and dismissed by rendering alone.
/// </summary>
public class ToastContainerTests
{
    /// <summary>A requested toast is rendered already visible, with no JavaScript call to reveal it.</summary>
    [Test]
    public async Task Toast_IsRenderedVisible_WithoutJsInterop()
    {
        await using var context = CreateContext(out var toasts);
        var cut = context.Render<ToastContainer>();

        toasts.ShowSuccess("Post saved.", "Saved");

        var toast = cut.WaitForElement(".toast");
        await Assert.That(toast.ClassList.Contains("show")).IsTrue();
        await Assert.That(toast.ClassList.Contains("text-bg-success")).IsTrue();
        await Assert.That(toast.TextContent).Contains("Post saved.");
        await Assert.That(context.JSInterop.Invocations).IsEmpty();
    }

    /// <summary>The close button removes only the toast it belongs to.</summary>
    [Test]
    public async Task CloseButton_RemovesToast()
    {
        await using var context = CreateContext(out var toasts);
        var cut = context.Render<ToastContainer>();

        toasts.ShowError("Upload failed.");
        toasts.ShowInfo("Draft restored.");
        cut.WaitForState(() => cut.FindAll(".toast").Count == 2);

        await cut.Find(".text-bg-danger .btn-close").ClickAsync(new MouseEventArgs());

        var remaining = cut.FindAll(".toast");
        await Assert.That(remaining.Count).IsEqualTo(1);
        await Assert.That(remaining[0].TextContent).Contains("Draft restored.");
    }

    /// <summary>
    /// Builds a context with the real toast service. JS interop is left in bUnit's default strict
    /// mode, so any interop call from the component would fail the test.
    /// </summary>
    private static BunitContext CreateContext(out IToastService toasts)
    {
        var context = new BunitContext();
        toasts = new ToastService();
        context.Services.AddSingleton(toasts);
        return context;
    }
}
