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
    [Arguments("/admin/settings")]
    public async Task Page_Anonymous_RedirectsToLogin(string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(response.Headers.Location!.AbsolutePath).IsEqualTo("/Account/Login").IgnoringCase();
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
