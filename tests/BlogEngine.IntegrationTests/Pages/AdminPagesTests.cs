using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Pages;

/// <summary>
/// Tests that the admin pages prerender on the server with their data (T1.8, T1.13–T1.15): the server
/// implementations of the admin services load the posts and settings, so the author sees content before
/// WebAssembly starts, and the pages are behind the <c>AdminOnly</c> policy.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Users)]
public class AdminPagesTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Recreates the admin and reader accounts before each test.</summary>
    [Before(Test)]
    public async Task ResetAccountsAsync()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
    }

    /// <summary>Anonymous visitors are sent to the login page from every new admin page.</summary>
    [Test]
    [Arguments("/admin/posts")]
    [Arguments("/admin/posts/new")]
    [Arguments("/admin/posts/1")]
    [Arguments("/admin/posts/1/revisions")]
    [Arguments("/admin/settings")]
    [Arguments("/admin/media")]
    [Arguments("/admin/media/1")]
    [Arguments("/admin/comments")]
    public async Task Page_Anonymous_RedirectsToLogin(string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location!.AbsolutePath).IsEqualTo("/Account/Login").IgnoringCase();
    }

    /// <summary>The revision history prerenders its list and the comparison with the newest revision (T4.3).</summary>
    [Test]
    public async Task Revisions_PrerendersComparison()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var post = await CreatePostAsync($"History {token}", [], $"Line one {token}");
        IReadOnlyList<PostRevisionSummaryDto> revisions;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var posts = scope.ServiceProvider.GetRequiredService<IPostAdminService>();
            post.ContentMarkdown = $"Line one {token}\n\nLine two {token}";
            await posts.UpdateAsync(post.Id, post);
            revisions = (await posts.GetRevisionsAsync(post.Id))!;
        }

        using var client = await CreateAdminClientAsync();
        var html = await GetHtmlAsync(client, $"/admin/posts/{post.Id}/revisions?revision={revisions[^1].Id}");

        await Assert.That(html).Contains("Revision history");
        await Assert.That(html).Contains("Restore this revision");
        await Assert.That(html).Contains($"Line two {token}");
        await Assert.That(html).Contains("diff-added");
    }

    /// <summary>The posts list prerenders with the posts, and the tag filter narrows it.</summary>
    [Test]
    public async Task PostsList_PrerendersFilteredPosts()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        await CreatePostAsync($"Listed {token}", ["Filter-" + token]);
        await CreatePostAsync($"Hidden {token}", []);
        using var client = await CreateAdminClientAsync();

        var all = await GetHtmlAsync(client, $"/admin/posts?search={token}");
        var tagged = await GetHtmlAsync(client, $"/admin/posts?search={token}&tag=filter-{token}");

        await Assert.That(all).Contains($"Listed {token}");
        await Assert.That(all).Contains($"Hidden {token}");
        await Assert.That(tagged).Contains($"Listed {token}");
        await Assert.That(tagged).DoesNotContain($"Hidden {token}");
    }

    /// <summary>The media library prerenders its grid, and the Unused filter narrows it (T2.6, T2.8).</summary>
    [Test]
    public async Task MediaLibrary_PrerendersFilteredGrid()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var used = await MediaTestFiles.AddAsync(factory, $"grid-used-{token}.png");
        var spare = await MediaTestFiles.AddAsync(factory, $"grid-spare-{token}.png");
        await CreatePostAsync($"Grid {token}", [], $"![x]({used.Path})");
        using var client = await CreateAdminClientAsync();

        var all = await GetHtmlAsync(client, $"/admin/media?search={token}");
        var unused = await GetHtmlAsync(client, $"/admin/media?search={token}&unused=true");

        await Assert.That(all).Contains(used.FileName);
        await Assert.That(all).Contains(spare.FileName);
        await Assert.That(all).Contains("Used in 1 post");
        await Assert.That(unused).DoesNotContain(used.FileName);
        await Assert.That(unused).Contains(spare.FileName);
    }

    /// <summary>The media editor prerenders the item's details and the posts that use it (T2.10).</summary>
    [Test]
    public async Task MediaEditor_PrerendersDetails()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var item = await MediaTestFiles.AddAsync(factory, $"editor-{token}.png", MediaTestFiles.Png(64, 48));
        await CreatePostAsync($"Uses editor image {token}", [], $"![x]({item.Path})");
        using var client = await CreateAdminClientAsync();

        var html = await GetHtmlAsync(client, $"/admin/media/{item.Id}");
        var missing = await GetHtmlAsync(client, $"/admin/media/{int.MaxValue}");

        await Assert.That(html).Contains(item.FileName);
        await Assert.That(html).Contains("64 × 48");
        await Assert.That(html).Contains($"Uses editor image {token}");
        await Assert.That(html).Contains($"value=\"{item.Path}\"");
        await Assert.That(missing).Contains("This image doesn't exist.");
    }

    /// <summary>The post editor's preview resolves library images while prerendering, like the published page.</summary>
    [Test]
    public async Task PostEditor_PrerendersLibraryImages()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var item = await MediaTestFiles.AddAsync(factory, $"preview-{token}.png", MediaTestFiles.Png(64, 48));
        var post = await CreatePostAsync($"Preview {token}", [], $"![Squares]({item.Path} \"Caption {token}\")");
        using var client = await CreateAdminClientAsync();

        var html = await GetHtmlAsync(client, $"/admin/posts/{post.Id}");

        await Assert.That(html).Contains($"src=\"/media/{item.PublicId}/{item.FileName}?v=1\"");
        await Assert.That(html).Contains($"<figcaption>Caption {token}</figcaption>");
    }

    /// <summary>The editor prerenders the stored post, including its Markdown and preview.</summary>
    [Test]
    public async Task PostEditor_PrerendersPost()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var post = await CreatePostAsync($"Editable {token}", ["Blazor"], $"## Section {token}");
        using var client = await CreateAdminClientAsync();

        var html = await GetHtmlAsync(client, $"/admin/posts/{post.Id}");

        await Assert.That(html).Contains($"value=\"Editable {token}\"");
        await Assert.That(html).Contains($"## Section {token}");
        await Assert.That(html).Contains($"id=\"section-{token}\" data-line=\"0\"");
        await Assert.That(html).Contains("Save draft");
    }

    /// <summary>A missing post shows a message rather than an empty editor.</summary>
    [Test]
    public async Task PostEditor_UnknownPost_ShowsNotFound()
    {
        using var client = await CreateAdminClientAsync();

        var html = await GetHtmlAsync(client, "/admin/posts/2147483647");

        await Assert.That(html).Contains("This post doesn't exist or is in the trash.");
    }

    /// <summary>A new post starts empty.</summary>
    [Test]
    public async Task PostEditor_New_PrerendersEmptyEditor()
    {
        using var client = await CreateAdminClientAsync();

        var html = await GetHtmlAsync(client, "/admin/posts/new");

        await Assert.That(html).Contains("New post");
        await Assert.That(html).Contains("Publish now");
    }

    /// <summary>The settings page prerenders the saved settings and the time zone picker.</summary>
    [Test]
    public async Task Settings_PrerendersSettings()
    {
        using var client = await CreateAdminClientAsync();

        var html = await GetHtmlAsync(client, "/admin/settings");

        await Assert.That(html).Contains("id=\"settings-site-title\"");
        await Assert.That(html).Contains("<option value=\"America/Chicago\"");
        await Assert.That(html).Contains("Save settings");
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        return client;
    }

    private static async Task<string> GetHtmlAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"GET {path} returned {(int)response.StatusCode}: {html}");
        }

        return System.Net.WebUtility.HtmlDecode(html);
    }

    private async Task<PostEditDto> CreatePostAsync(string title, List<string> tags, string markdown = "Body")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostAdminService>();
        var result = await posts.CreateAsync(new PostEditDto { Title = title, Tags = tags, ContentMarkdown = markdown });

        return ((PostSaved)result).Post;
    }
}
