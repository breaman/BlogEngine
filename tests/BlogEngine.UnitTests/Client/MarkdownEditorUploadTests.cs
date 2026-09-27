using System.Text.Json;

using BlogEngine.Client.Components;
using BlogEngine.Client.Services;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Bunit;

using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.UnitTests.Client;

/// <summary>
/// Tests the .NET side of pasting or dropping an image into <see cref="MarkdownEditor"/> (design 9.5, A10, T4.2):
/// editor.js is told where to upload, and each upload response becomes the Markdown that replaces the placeholder, or
/// an error toast.
/// </summary>
public class MarkdownEditorUploadTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>With uploads on, editor.js gets the media endpoint and the antiforgery header to send.</summary>
    [Test]
    public async Task AllowImageUpload_PassesUploadOptions()
    {
        await using var context = CreateContext(out _);
        var module = SetupEditorModule(context);

        context.Render<MarkdownEditor>(parameters => parameters.Add(p => p.AllowImageUpload, true));

        var options = JsonSerializer.SerializeToElement(module.Invocations["createEditor"].Single().Arguments[2], JsonOptions);
        var uploads = options.GetProperty("uploads");
        await Assert.That(uploads.GetProperty("url").GetString()).IsEqualTo(ClientMediaService.BaseUri);
        await Assert.That(uploads.GetProperty("token").GetString()).IsEqualTo("token-123");
    }

    /// <summary>Without it, paste and drop are left to CodeMirror.</summary>
    [Test]
    public async Task WithoutImageUpload_NoUploadOptions()
    {
        await using var context = CreateContext(out _);
        var module = SetupEditorModule(context);

        context.Render<MarkdownEditor>();

        var options = JsonSerializer.SerializeToElement(module.Invocations["createEditor"].Single().Arguments[2], JsonOptions);
        await Assert.That(options.GetProperty("uploads").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    /// <summary>
    /// A successful upload becomes standard Markdown for the library image, the preview can resolve it at once, and the
    /// author is reminded to add alt text.
    /// </summary>
    [Test]
    public async Task SuccessfulUpload_ReturnsImageMarkdown()
    {
        await using var context = CreateContext(out var toasts);
        SetupEditorModule(context);
        var cut = context.Render<MarkdownEditor>(parameters => parameters.Add(p => p.AllowImageUpload, true));
        var body = JsonSerializer.Serialize(new[] { new MediaUploadResult { FileName = "Screen Shot.png", Item = Item() } }, JsonOptions);

        var markdown = await cut.InvokeAsync(() => cut.Instance.OnImageUploaded("Screen Shot.png", 200, body));

        await Assert.That(markdown).IsEqualTo("![](/media/ab12cd34ef56/screen-shot.png)");
        await Assert.That(context.Services.GetRequiredService<MediaLookupCache>().Find("ab12cd34ef56")).IsNotNull();
        await Assert.That(toasts.Single().Type).IsEqualTo(ToastType.Info);
    }

    /// <summary>A file the server rejected returns no Markdown (editor.js removes the placeholder) and shows the reason.</summary>
    [Test]
    public async Task RejectedFile_ShowsReason()
    {
        await using var context = CreateContext(out var toasts);
        SetupEditorModule(context);
        var cut = context.Render<MarkdownEditor>(parameters => parameters.Add(p => p.AllowImageUpload, true));
        var body = JsonSerializer.Serialize(new[] { new MediaUploadResult { FileName = "photo.heic", Error = "HEIC images aren't supported." } }, JsonOptions);

        var markdown = await cut.InvokeAsync(() => cut.Instance.OnImageUploaded("photo.heic", 200, body));

        await Assert.That(markdown).IsNull();
        await Assert.That(toasts.Single().Type).IsEqualTo(ToastType.Error);
        await Assert.That(toasts.Single().Message).IsEqualTo("HEIC images aren't supported.");
        await Assert.That(toasts.Single().Heading).IsEqualTo("photo.heic wasn't uploaded");
    }

    /// <summary>A request that failed outright (network error, expired session) is explained too.</summary>
    [Test]
    [Arguments(0, "Check your connection")]
    [Arguments(403, "Sign in again")]
    public async Task FailedRequest_ShowsReason(int status, string expected)
    {
        await using var context = CreateContext(out var toasts);
        SetupEditorModule(context);
        var cut = context.Render<MarkdownEditor>(parameters => parameters.Add(p => p.AllowImageUpload, true));

        var markdown = await cut.InvokeAsync(() => cut.Instance.OnImageUploaded("a.png", status, string.Empty));

        await Assert.That(markdown).IsNull();
        await Assert.That(toasts.Single().Message).Contains(expected);
    }

    private static MediaItemDto Item()
    {
        return new MediaItemDto
        {
            Id = 9,
            PublicId = "ab12cd34ef56",
            FileName = "screen-shot.png",
            Url = "/media/ab12cd34ef56/screen-shot.png?v=1",
            Path = "/media/ab12cd34ef56/screen-shot.png",
            Width = 800,
            Height = 600,
            Version = 1
        };
    }

    /// <summary>editor.js's module, with <c>createEditor</c> returning an editor whose other calls are ignored.</summary>
    private static BunitJSModuleInterop SetupEditorModule(BunitContext context)
    {
        var module = context.JSInterop.SetupModule("./js/editor.js");
        module.SetupModule("createEditor", _ => true).Mode = JSRuntimeMode.Loose;
        return module;
    }

    private static BunitContext CreateContext(out List<ToastMessage> toasts)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var toastService = new ToastService();
        var shown = new List<ToastMessage>();
        toastService.OnToastAdded += shown.Add;
        toasts = shown;

        context.Services.AddSingleton<IToastService>(toastService);
        context.Services.AddSingleton<IMediaService>(new FakeMediaService());
        context.Services.AddScoped<MediaLookupCache>();
        context.Services.AddScoped<RecentMediaStore>();
        context.Services.AddSingleton<AntiforgeryStateProvider>(new FakeAntiforgeryStateProvider("token-123"));
        return context;
    }

    /// <summary>Supplies a fixed antiforgery token.</summary>
    private sealed class FakeAntiforgeryStateProvider(string token) : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken()
        {
            return new AntiforgeryRequestToken(token, "__RequestVerificationToken");
        }
    }
}