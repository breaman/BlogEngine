using System.IO.Compression;
using System.Net;
using System.Text.Json;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests the export download (design 7.4, 17, O7, T4.24): <c>GET /api/admin/export</c> streams a zip with the posts and
/// pages as Markdown with front matter, the media originals with their metadata, and the comments and settings as JSON.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Users)]
public class ExportTests(BlogEngineWebApplicationFactory factory)
{
    private const string ExportApi = "/api/admin/export";

    /// <summary>Recreates the admin and reader accounts before each test.</summary>
    [Before(Test)]
    public async Task ResetAccountsAsync()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
    }

    /// <summary>The export holds drafts and emails, so anonymous callers get 401 and non-admins 403.</summary>
    [Test]
    public async Task Export_NonAdmins_AreRejected()
    {
        using var anonymous = IdentityTestHelper.CreateClient(factory);
        using var reader = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(reader, IdentityTestHelper.ReaderEmail);

        using var anonymousResponse = await anonymous.GetAsync(ExportApi);
        using var readerResponse = await reader.GetAsync(ExportApi);

        await Assert.That(anonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(readerResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The T4.24 "done when": the zip opens, and a post's front matter has its title, slug, date, tags, summary, status and
    /// cover. Drafts, pages, media, comments and settings are in it too; posts in the trash are not.
    /// </summary>
    [Test]
    public async Task Export_ZipContainsTheWholeBlog()
    {
        var token = PublicTestPosts.Token();
        var cover = await MediaTestFiles.AddAsync(factory, $"export-cover-{token}.png");
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Export {token}: \"quoted\"",
            Summary = $"Summary of {token}.",
            Tags = ["C#", $"export{token}"],
            CoverMediaId = cover.Id,
            ContentMarkdown = $"Body of {token}.\n\nSecond paragraph."
        }, PublicTestPosts.Noon(PublicTestPosts.NextYear(), 5, 6));
        var draft = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Export draft {token}" });
        var trashed = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Export trashed {token}" });
        await PublicTestPosts.TrashAsync(factory, trashed.Id);
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, $"Comment {token}", email: $"reader-{token}@example.com");
        PageEditDto page;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var pages = scope.ServiceProvider.GetRequiredService<IPageAdminService>();
            page = ((PageSaved)await pages.CreateAsync(new PageEditDto { Title = $"Export page {token}", ContentMarkdown = $"Page {token}" })).Page;
        }

        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        using var response = await client.GetAsync(ExportApi);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/zip");
        await Assert.That(response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName)
            .Matches(@"^""?blog-export-\d{4}-\d{2}-\d{2}\.zip""?$");
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();

        var postFile = Read(zip, $"posts/{post.PublishedDateLocal:yyyy-MM-dd}-{post.Slug}.md");
        await Assert.That(postFile).StartsWith("---\n");
        await Assert.That(postFile).Contains($"title: \"Export {token}: \\\"quoted\\\"\"\n");
        await Assert.That(postFile).Contains($"slug: \"{post.Slug}\"\n");
        await Assert.That(postFile).Contains($"date: {post.PublishedOn!.Value:yyyy-MM-dd'T'HH:mm:ss}+00:00\n");
        await Assert.That(postFile).Contains($"tags:\n  - \"C#\"\n  - \"export{token}\"\n");
        await Assert.That(postFile).Contains($"summary: \"Summary of {token}.\"\n");
        await Assert.That(postFile).Contains("status: \"published\"\n");
        await Assert.That(postFile).Contains("draft: false\n");
        await Assert.That(postFile).Contains($"cover: \"{MediaPaths.Item(cover.PublicId, cover.FileName)}\"\n");
        await Assert.That(postFile).EndsWith($"---\n\nBody of {token}.\n\nSecond paragraph.\n");

        var draftFile = Read(zip, $"posts/{draft.Slug}.md");
        await Assert.That(draftFile).Contains("status: \"draft\"\n");
        await Assert.That(draftFile).Contains("draft: true\n");
        await Assert.That(zip.Entries.Any(e => e.FullName.Contains(trashed.Slug!, StringComparison.Ordinal))).IsFalse();

        await Assert.That(Read(zip, $"pages/{page.Slug}.md")).Contains($"title: \"Export page {token}\"\n");

        var original = zip.GetEntry($"media/{cover.PublicId}/original.png");
        await Assert.That(original).IsNotNull();
        await using (var stream = await original!.OpenAsync())
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            await Assert.That(copy.ToArray()).IsEquivalentTo((await MediaTestFiles.ReadStoredAsync(factory, $"{cover.PublicId}/original.png"))!);
        }

        using var metadata = JsonDocument.Parse(Read(zip, $"media/{cover.PublicId}/metadata.json"));
        await Assert.That(metadata.RootElement.GetProperty("fileName").GetString()).IsEqualTo(cover.FileName);
        await Assert.That(metadata.RootElement.GetProperty("original").GetString()).IsEqualTo("original.png");

        using var comments = JsonDocument.Parse(Read(zip, "comments.json"));
        var comment = comments.RootElement.EnumerateArray().Single(c => c.GetProperty("bodyMarkdown").GetString() == $"Comment {token}");
        await Assert.That(comment.GetProperty("postSlug").GetString()).IsEqualTo(post.Slug);
        await Assert.That(comment.GetProperty("authorEmail").GetString()).IsEqualTo($"reader-{token}@example.com");
        await Assert.That(comment.GetProperty("status").GetString()).IsEqualTo("approved");
        await Assert.That(comment.TryGetProperty("ipHash", out _)).IsFalse();

        using var tags = JsonDocument.Parse(Read(zip, "tags.json"));
        await Assert.That(tags.RootElement.EnumerateArray().Any(t => t.GetProperty("name").GetString() == $"export{token}")).IsTrue();

        using var settings = JsonDocument.Parse(Read(zip, "settings.json"));
        await Assert.That(settings.RootElement.GetProperty("siteTitle").GetString()).IsNotNull();
        await Assert.That(zip.GetEntry("redirects.json")).IsNotNull();
    }

    /// <summary>The text of a zip entry, which must exist.</summary>
    private static string Read(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"The export has no {name}.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}