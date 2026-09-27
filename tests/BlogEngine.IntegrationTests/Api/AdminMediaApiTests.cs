using System.Net;
using System.Net.Http.Json;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Security;

using Microsoft.AspNetCore.Mvc;

using SixLabors.ImageSharp;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests the admin media endpoints over HTTP (design 7.4, 9; T2.3, T2.5, T2.10): authorization, uploads with
/// per-file results (a valid photo with GPS removed, a renamed non-image, an oversized file), listing with the unused
/// filter, metadata, lookups, edits, and both delete paths.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Users)]
public class AdminMediaApiTests(BlogEngineWebApplicationFactory factory)
{
    private const string MediaApi = MediaTestFiles.MediaApi;

    /// <summary>Every media endpoint, as (method, path).</summary>
    public static IEnumerable<(string Method, string Path)> Endpoints()
    {
        yield return ("GET", MediaApi);
        yield return ("GET", $"{MediaApi}/1");
        yield return ("GET", $"{MediaApi}/lookup?ids=aaaaaaaaaaaa");
        yield return ("GET", $"{MediaApi}/1/original");
        yield return ("POST", MediaApi);
        yield return ("PUT", $"{MediaApi}/1");
        yield return ("POST", $"{MediaApi}/1/edit");
        yield return ("POST", $"{MediaApi}/1/revert");
        yield return ("POST", $"{MediaApi}/1/copy");
        yield return ("GET", $"{MediaApi}/renditions");
        yield return ("POST", $"{MediaApi}/renditions");
        yield return ("DELETE", $"{MediaApi}/1");
    }

    /// <summary>Recreates the admin and reader accounts before each test.</summary>
    [Before(Test)]
    public async Task ResetAccountsAsync()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
    }

    /// <summary>Anonymous callers get 401.</summary>
    [Test]
    [MethodDataSource(nameof(Endpoints))]
    public async Task Endpoint_Anonymous_Returns401(string method, string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.SendAsync(CreateRequest(method, path));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    /// <summary>Signed-in users without the Admin role get 403.</summary>
    [Test]
    [MethodDataSource(nameof(Endpoints))]
    public async Task Endpoint_NonAdmin_Returns403(string method, string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.ReaderEmail);

        using var response = await client.SendAsync(CreateRequest(method, path));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>Uploads need the antiforgery token like every other mutation.</summary>
    [Test]
    public async Task Upload_WithoutToken_IsRejected()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);

        using var response = await MediaTestFiles.UploadAsync(client, antiforgeryToken: null, ("a.png", MediaTestFiles.Png()));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A sideways phone photo is stored upright with its GPS location removed, under a random public id and a
    /// slugified name (M5).
    /// </summary>
    [Test]
    public async Task Upload_Photo_IsOrientedAndStrippedOfGps()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var name = $"Holiday {MediaTestFiles.Token()}.JPG";

        using var response = await MediaTestFiles.UploadAsync(client, token, (name, MediaTestFiles.SidewaysJpegWithGps(400, 200)));
        var result = (await response.Content.ReadFromJsonAsync<List<MediaUploadResult>>())!.Single();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(result.Error).IsNull();
        var item = result.Item!;
        await Assert.That((item.Width, item.Height)).IsEqualTo((200, 400));
        await Assert.That(item.ContentType).IsEqualTo("image/jpeg");
        await Assert.That(item.PublicId).Matches("^[a-z0-9]{12}$");
        await Assert.That(item.FileName).IsEqualTo(name[..^4].ToLowerInvariant().Replace(' ', '-') + ".jpg");
        await Assert.That(item.Url).IsEqualTo($"/media/{item.PublicId}/{item.FileName}?v=1");

        var stored = await MediaTestFiles.ReadStoredAsync(factory, $"{item.PublicId}/original.jpg");
        var info = Image.Identify(stored!);
        await Assert.That(info.Metadata.ExifProfile).IsNull();
        await Assert.That((info.Width, info.Height)).IsEqualTo((200, 400));
    }

    /// <summary>Each file gets its own result: a renamed text file fails without stopping the image next to it.</summary>
    [Test]
    public async Task Upload_RenamedNonImage_FailsAlone()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await MediaTestFiles.UploadAsync(client, token,
            ("notes.jpg", "These are notes, not a photo."u8.ToArray()),
            ($"ok-{MediaTestFiles.Token()}.png", MediaTestFiles.Png()));
        var results = (await response.Content.ReadFromJsonAsync<List<MediaUploadResult>>())!;

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(results[0].Item).IsNull();
        await Assert.That(results[0].Error!).Contains("isn't an image");
        await Assert.That(results[1].Item!.ContentType).IsEqualTo("image/png");
    }

    /// <summary>A file over the size limit is refused before it is decoded.</summary>
    [Test]
    public async Task Upload_Oversized_IsRejected()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await MediaTestFiles.UploadAsync(client, token, ("huge.jpg", new byte[21 * 1024 * 1024]));
        var result = (await response.Content.ReadFromJsonAsync<List<MediaUploadResult>>())!.Single();

        await Assert.That(result.Item).IsNull();
        await Assert.That(result.Error!).Contains("20 MB");
    }

    /// <summary>A request without files is a validation problem.</summary>
    [Test]
    public async Task Upload_NoFiles_Returns400()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        using var request = new HttpRequestMessage(HttpMethod.Post, MediaApi)
        {
            Content = new MultipartFormDataContent { { new StringContent("x"), "note" } }
        };
        request.Headers.Add(AntiforgeryHeaders.RequestToken, token);

        using var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(problem!.Errors.Keys).IsEquivalentTo(["files"]);
    }

    /// <summary>Uploading the same image twice works but reports the existing copy.</summary>
    [Test]
    public async Task Upload_Duplicate_ReportsExistingItem()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var bytes = MediaTestFiles.Png(33, 17 + Random.Shared.Next(50));
        var first = await MediaTestFiles.AddAsync(factory, $"first-{MediaTestFiles.Token()}.png", bytes);

        using var response = await MediaTestFiles.UploadAsync(client, token, ("again.png", bytes));
        var result = (await response.Content.ReadFromJsonAsync<List<MediaUploadResult>>())!.Single();

        await Assert.That(result.Item).IsNotNull();
        await Assert.That(result.Duplicates.Select(d => d.Id)).Contains(first.Id);
    }

    /// <summary>The list searches names and alt text and can show only unused items.</summary>
    [Test]
    public async Task List_SearchAndUnusedFilter()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var marker = MediaTestFiles.Token();
        var used = await MediaTestFiles.AddAsync(factory, $"used-{marker}.png");
        var unused = await MediaTestFiles.AddAsync(factory, $"spare-{marker}.png");
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Uses {marker}", ContentMarkdown = $"![x]({used.Path})" });

        var all = await client.GetFromJsonAsync<PagedResult<MediaItemDto>>($"{MediaApi}?search={marker}");
        var unusedOnly = await client.GetFromJsonAsync<PagedResult<MediaItemDto>>($"{MediaApi}?search={marker}&unused=true");

        await Assert.That(all!.Items.Select(i => i.Id)).IsEquivalentTo([used.Id, unused.Id]);
        await Assert.That(all.Items.Single(i => i.Id == used.Id).UsageCount).IsEqualTo(1);
        await Assert.That(unusedOnly!.Items.Select(i => i.Id)).IsEquivalentTo([unused.Id]);
    }

    /// <summary>PUT saves alt text and caption; invalid lengths are a 400; unknown ids a 404.</summary>
    [Test]
    public async Task Update_SavesMetadata()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var item = await MediaTestFiles.AddAsync(factory, $"meta-{MediaTestFiles.Token()}.png");

        using var ok = await SendAsync(client, token, HttpMethod.Put, $"{MediaApi}/{item.Id}",
            new MediaUpdateRequest { AltText = "  A red and blue square  ", Caption = "Colors" });
        using var invalid = await SendAsync(client, token, HttpMethod.Put, $"{MediaApi}/{item.Id}",
            new MediaUpdateRequest { AltText = new string('a', 301) });
        using var missing = await SendAsync(client, token, HttpMethod.Put, $"{MediaApi}/{int.MaxValue}", new MediaUpdateRequest());
        var loaded = await client.GetFromJsonAsync<MediaItemDto>($"{MediaApi}/{item.Id}");

        await Assert.That(ok.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(loaded!.AltText).IsEqualTo("A red and blue square");
        await Assert.That(loaded.Caption).IsEqualTo("Colors");
    }

    /// <summary>Lookups return what the preview needs and skip unknown ids.</summary>
    [Test]
    public async Task Lookup_ReturnsKnownItems()
    {
        var (client, _) = await LoginAdminAsync();
        using var __ = client;
        var item = await MediaTestFiles.AddAsync(factory, $"lookup-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(64, 32));

        var found = await client.GetFromJsonAsync<List<MediaLookupItem>>($"{MediaApi}/lookup?ids={item.PublicId}&ids=zzzzzzzzzzzz");

        var single = found!.Single();
        await Assert.That(single with { RenditionWidths = null }).IsEqualTo(new MediaLookupItem(item.Id, item.PublicId, item.FileName, 64, 32, 1, ""));
        await Assert.That(single.Renditions).IsEquivalentTo([64]);
    }

    /// <summary>An unused item is deleted with all its files.</summary>
    [Test]
    public async Task Delete_Unused_RemovesRowAndFiles()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var item = await MediaTestFiles.AddAsync(factory, $"delete-{MediaTestFiles.Token()}.png");

        using var response = await SendAsync(client, token, HttpMethod.Delete, $"{MediaApi}/{item.Id}");
        using var afterwards = await client.GetAsync($"{MediaApi}/{item.Id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(afterwards.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(Directory.Exists(Path.Combine(factory.MediaRoot, item.PublicId))).IsFalse();
    }

    /// <summary>
    /// A used item is kept and the posts using it are listed (409); with <c>force</c> it is deleted and the published
    /// post silently drops the image (design 9.6).
    /// </summary>
    [Test]
    public async Task Delete_Used_RequiresForce()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var marker = MediaTestFiles.Token();
        var item = await MediaTestFiles.AddAsync(factory, $"in-use-{marker}.png");
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Gallery {marker}",
            ContentMarkdown = $"Intro.\n\n![Squares]({item.Path})"
        }, PastDate());
        var before = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        using var refused = await SendAsync(client, token, HttpMethod.Delete, $"{MediaApi}/{item.Id}");
        var usedBy = await refused.Content.ReadFromJsonAsync<List<MediaUsageDto>>();
        using var forced = await SendAsync(client, token, HttpMethod.Delete, $"{MediaApi}/{item.Id}?force=true");
        var after = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(before).Contains($"/media/{item.PublicId}/");
        await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(usedBy!.Select(p => p.Title)).IsEquivalentTo([post.Title]);
        await Assert.That(forced.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(after).DoesNotContain(item.PublicId);
        await Assert.That(after).Contains("Intro.");
    }

    /// <summary>
    /// Editing applies rotate, crop and resize to the original and bumps the version; a published post that uses the
    /// image shows the new <c>?v=</c> and size (T2.10's done-when).
    /// </summary>
    [Test]
    public async Task Edit_CreatesNewVersion_AndUpdatesPosts()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var marker = MediaTestFiles.Token();
        var item = await MediaTestFiles.AddAsync(factory, $"edit-{marker}.png", MediaTestFiles.Png(400, 200));
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Edited {marker}", ContentMarkdown = $"![Squares]({item.Path})" }, PastDate());

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{MediaApi}/{item.Id}/edit", new MediaEditOperations
        {
            Rotate = 90,
            Crop = new MediaCropRect { X = 0, Y = 0, Width = 200, Height = 200 },
            Resize = new MediaSize { Width = 100, Height = 100 }
        });
        var edited = await response.Content.ReadFromJsonAsync<MediaItemDto>();
        var page = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);
        var original = await client.GetByteArrayAsync($"{MediaApi}/{item.Id}/original");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((edited!.Width, edited.Height, edited.Version)).IsEqualTo((100, 100, 2));
        await Assert.That(edited.EditOperations!.Rotate).IsEqualTo(90);
        await Assert.That(page).Contains($"src=\"/media/{item.PublicId}/{item.FileName}?w=100&amp;v=2\"");
        await Assert.That(page).Contains("width=\"100\" height=\"100\"");
        await Assert.That(Image.Identify(original).Width).IsEqualTo(400);
    }

    /// <summary>A crop outside the image is a validation problem, and nothing changes.</summary>
    [Test]
    public async Task Edit_CropOutsideImage_Returns400()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var item = await MediaTestFiles.AddAsync(factory, $"bad-edit-{MediaTestFiles.Token()}.png", MediaTestFiles.Png(40, 20));

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{MediaApi}/{item.Id}/edit",
            new MediaEditOperations { Crop = new MediaCropRect { X = 30, Y = 0, Width = 20, Height = 20 } });
        var unchanged = await client.GetFromJsonAsync<MediaItemDto>($"{MediaApi}/{item.Id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(unchanged!.Version).IsEqualTo(1);
    }

    /// <summary>A publish date in a year of its own, so these posts don't crowd other tests' feeds and lists.</summary>
    private static DateTimeOffset PastDate()
    {
        return PublicTestPosts.Noon(PublicTestPosts.NextYear(), 6, 1);
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

    /// <summary>A request with a minimal body for writes, so it reaches authorization rather than failing binding.</summary>
    private static HttpRequestMessage CreateRequest(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { altText = "x" });
        }

        return request;
    }
}