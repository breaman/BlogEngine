using System.Net;
using System.Net.Http.Json;

using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests responsive renditions (design 9.4, M6, T4.19) and non-destructive editing (design 9.2, M7, T4.18): renditions
/// are made on upload and edit, served by <c>?w=</c>/<c>?f=</c>, listed in the post's <c>&lt;picture&gt;</c> and
/// backfilled for older images; Revert restores the original and Save as copy leaves the source alone.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Users)]
public class MediaRenditionsTests(BlogEngineWebApplicationFactory factory)
{
    private const string MediaApi = MediaTestFiles.MediaApi;

    /// <summary>Recreates the admin account before each test.</summary>
    [Before(Test)]
    public async Task ResetAccountsAsync()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
    }

    /// <summary>An upload gets WebP and own-format renditions at each width up to its size, stored under its version.</summary>
    [Test]
    public async Task Upload_CreatesRenditions()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"wide-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(1000, 500));

        var renditions = await RenditionsAsync(item.Id);

        await Assert.That(item.RenditionWidths).IsEquivalentTo([320, 640, 960, 1000]);
        await Assert.That(renditions.Select(r => (r.Width, r.Format)))
            .IsEquivalentTo([(320, "webp"), (320, "png"), (640, "webp"), (640, "png"), (960, "webp"), (960, "png"), (1000, "webp"), (1000, "png")]);
        foreach (var rendition in renditions)
        {
            await Assert.That(rendition.StorageKey).StartsWith($"{item.PublicId}/v1/");
            await Assert.That(await MediaTestFiles.ReadStoredAsync(factory, rendition.StorageKey)).IsNotNull();
        }
    }

    /// <summary>
    /// <c>?w=</c> serves the nearest rendition at least that wide and <c>?f=webp</c> its WebP copy, with its own ETag and
    /// the year-long cache for the current version (T4.19's done-when, from the server side).
    /// </summary>
    [Test]
    public async Task MediaEndpoint_ServesRenditionByWidthAndFormat()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"served-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(1000, 500));
        using var client = factory.CreateClient();

        using var webp = await client.GetAsync($"{item.Path}?w=500&v=1&f=webp");
        using var png = await client.GetAsync($"{item.Path}?w=500&v=1");
        using var full = await client.GetAsync($"{item.Path}?v=1");
        var webpBytes = await webp.Content.ReadAsByteArrayAsync();
        var pngBytes = await png.Content.ReadAsByteArrayAsync();

        await Assert.That(webp.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(webp.Content.Headers.ContentType!.MediaType).IsEqualTo("image/webp");
        await Assert.That(Image.Identify(webpBytes).Metadata.DecodedImageFormat).IsEqualTo(WebpFormat.Instance);
        await Assert.That(Image.Identify(webpBytes).Width).IsEqualTo(640);
        await Assert.That(png.Content.Headers.ContentType!.MediaType).IsEqualTo("image/png");
        await Assert.That(Image.Identify(pngBytes).Metadata.DecodedImageFormat).IsEqualTo(PngFormat.Instance);
        await Assert.That(Image.Identify(pngBytes).Width).IsEqualTo(640);
        await Assert.That(Image.Identify(await full.Content.ReadAsByteArrayAsync()).Width).IsEqualTo(1000);
        await Assert.That(webp.Headers.CacheControl!.ToString()).Contains("immutable");
        await Assert.That(webp.Headers.ETag!.Tag).IsNotEqualTo(full.Headers.ETag!.Tag);
        await Assert.That(webp.Headers.ETag.Tag).IsNotEqualTo(png.Headers.ETag!.Tag);
    }

    /// <summary>A published post lists the renditions in a <c>&lt;picture&gt;</c> with a WebP source.</summary>
    [Test]
    public async Task PublishedPost_UsesPictureWithSrcSet()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"pictured-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(1000, 500));
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Pictured {MediaTestFiles.Token()}", ContentMarkdown = $"![Squares]({item.Path})" },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 6, 1));
        using var client = factory.CreateClient();

        var page = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(page).Contains("<picture><source type=\"image/webp\"");
        await Assert.That(page).Contains($"{item.Path}?w=640&amp;v=1&amp;f=webp 640w");
        await Assert.That(page).Contains($"src=\"{item.Path}?w=1000&amp;v=1\"");
        await Assert.That(page).Contains("width=\"1000\" height=\"500\"");
    }

    /// <summary>An edit makes renditions of the new version and removes the old version's files.</summary>
    [Test]
    public async Task Edit_ReplacesRenditions()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"resized-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(1000, 500));
        var before = await RenditionsAsync(item.Id);
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{MediaApi}/{item.Id}/edit",
            new MediaEditOperations { Resize = new MediaSize { Width = 700, Height = 350 } });
        var edited = await response.Content.ReadFromJsonAsync<MediaItemDto>();
        var after = await RenditionsAsync(item.Id);

        await Assert.That(edited!.RenditionWidths).IsEquivalentTo([320, 640, 700]);
        await Assert.That(after.Select(r => r.StorageKey)).All(k => k.StartsWith($"{item.PublicId}/v2/", StringComparison.Ordinal));
        foreach (var old in before)
        {
            await Assert.That(await MediaTestFiles.ReadStoredAsync(factory, old.StorageKey)).IsNull();
        }
    }

    /// <summary>Revert brings back the original's dimensions under a new version and drops the edits (T4.18's done-when).</summary>
    [Test]
    public async Task Revert_RestoresOriginal_AndBumpsVersion()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"revert-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(400, 200));
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var edit = await SendAsync(client, token, HttpMethod.Post, $"{MediaApi}/{item.Id}/edit",
            new MediaEditOperations { Crop = new MediaCropRect { X = 0, Y = 0, Width = 100, Height = 100 } });
        using var revert = await SendAsync(client, token, HttpMethod.Post, $"{MediaApi}/{item.Id}/revert");
        var reverted = await revert.Content.ReadFromJsonAsync<MediaItemDto>();
        using var missing = await SendAsync(client, token, HttpMethod.Post, $"{MediaApi}/999999999/revert");

        await Assert.That(edit.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(revert.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((reverted!.Width, reverted.Height, reverted.Version)).IsEqualTo((400, 200, 3));
        await Assert.That(reverted.EditOperations).IsNull();
        await Assert.That(reverted.RenditionWidths).IsEquivalentTo([320, 400]);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Save as copy makes a new item from the original plus the operations, with the same alt text; the source and the
    /// posts using it are unchanged.
    /// </summary>
    [Test]
    public async Task SaveAsCopy_CreatesNewItem_LeavesSourceAlone()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"source-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(400, 200));
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        using var described = await SendAsync(client, token, HttpMethod.Put, $"{MediaApi}/{item.Id}",
            new MediaUpdateRequest { AltText = "Red and blue", Caption = "Colors" });

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{MediaApi}/{item.Id}/copy", new MediaEditOperations { Rotate = 90 });
        var copy = await response.Content.ReadFromJsonAsync<MediaItemDto>();
        var source = await client.GetFromJsonAsync<MediaItemDto>($"{MediaApi}/{item.Id}");
        var copyOriginal = await client.GetByteArrayAsync($"{MediaApi}/{copy!.Id}/original");
        var sourceOriginal = await client.GetByteArrayAsync($"{MediaApi}/{item.Id}/original");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(copy.Id).IsNotEqualTo(item.Id);
        await Assert.That(copy.PublicId).IsNotEqualTo(item.PublicId);
        await Assert.That((copy.Width, copy.Height, copy.Version)).IsEqualTo((200, 400, 1));
        await Assert.That(copy.EditOperations!.Rotate).IsEqualTo(90);
        await Assert.That((copy.AltText, copy.Caption)).IsEqualTo(("Red and blue", "Colors"));
        await Assert.That(copy.RenditionWidths).IsEquivalentTo([200]);
        await Assert.That((source!.Width, source.Height, source.Version)).IsEqualTo((400, 200, 1));
        await Assert.That(copyOriginal).IsEquivalentTo(sourceOriginal);
    }

    /// <summary>
    /// An image without renditions (stored before they existed) is counted, backfilled in batches, and the posts using it
    /// are re-rendered with the new <c>srcset</c>.
    /// </summary>
    [Test]
    public async Task Backfill_GeneratesMissingRenditions_AndRerendersPosts()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"older-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(800, 400));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.MediaRenditions.Where(r => r.MediaItemId == item.Id).ExecuteDeleteAsync();
        }

        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Older image {MediaTestFiles.Token()}", ContentMarkdown = $"![Squares]({item.Path})" },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 6, 1));
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var pageBefore = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        var progress = await client.GetFromJsonAsync<MediaRenditionProgress>($"{MediaApi}/renditions");
        for (var i = 0; i < 20 && progress!.Remaining > 0; i++)
        {
            using var batch = await SendAsync(client, token, HttpMethod.Post, $"{MediaApi}/renditions?max=10");
            var next = await batch.Content.ReadFromJsonAsync<MediaRenditionProgress>();
            if (next!.Processed == 0)
            {
                break;
            }

            progress = next;
        }

        var pageAfter = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);
        var item2 = await client.GetFromJsonAsync<MediaItemDto>($"{MediaApi}/{item.Id}");

        await Assert.That(pageBefore).DoesNotContain($"{item.Path}?w=");
        await Assert.That(item2!.RenditionWidths).IsEquivalentTo([320, 640, 800]);
        await Assert.That(pageAfter).Contains($"{item.Path}?w=640&amp;v=1&amp;f=webp 640w");
    }

    private async Task<List<MediaRendition>> RenditionsAsync(int mediaItemId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().MediaRenditions
            .AsNoTracking()
            .Where(r => r.MediaItemId == mediaItemId)
            .OrderBy(r => r.Width).ThenBy(r => r.Format)
            .ToListAsync();
    }

    private async Task<(HttpClient Client, string Token)> LoginAdminAsync()
    {
        var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        return (client, await IdentityTestHelper.GetAntiforgeryTokenAsync(client, "/admin"));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        request.Headers.Add(AntiforgeryHeaders.RequestToken, token);
        return client.SendAsync(request);
    }
}
