using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Enums;

namespace BlogEngine.IntegrationTests.Pages;

/// <summary>
/// Tests that the comment admin pages prerender with their data (T3.8–T3.10): the moderation queue and its tabs, the
/// blocklist, the dashboard counts and the pending badge in the admin nav, which anonymous visitors never see.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel([TestConstraints.Users, TestConstraints.Comments])]
public class AdminCommentPagesTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>Recreates the admin and reader accounts before each test.</summary>
    [Before(Test)]
    public async Task ResetAccountsAsync()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
    }

    /// <summary>The queue opens on Pending with the comment, its author, email, post and spam details, and every action.</summary>
    [Test]
    public async Task Queue_PrerendersPendingComments()
    {
        var token = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Queue post {token}");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, $"Queued comment {token}", email: $"queue-{token}@example.com",
            name: $"Queued {token}");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Spam, $"Spam comment {token}");
        using var client = await CreateAdminClientAsync();

        var pending = await GetHtmlAsync(client, "/admin/comments");
        var spam = await GetHtmlAsync(client, "/admin/comments?tab=spam");

        await Assert.That(pending).Contains($"Queued comment {token}");
        await Assert.That(pending).Contains($"queue-{token}@example.com");
        await Assert.That(pending).Contains($"Queue post {token}");
        await Assert.That(pending).Contains("Block commenter");
        await Assert.That(pending).Contains("Approve");
        await Assert.That(pending).DoesNotContain($"Spam comment {token}");
        await Assert.That(spam).Contains($"Spam comment {token}");
        await Assert.That(spam).Contains("Empty spam");
    }

    /// <summary>The blocklist tab shows the add form.</summary>
    [Test]
    public async Task Blocklist_Prerenders()
    {
        using var client = await CreateAdminClientAsync();

        var html = await GetHtmlAsync(client, "/admin/comments?tab=blocklist");

        await Assert.That(html).Contains("Add a block");
        await Assert.That(html).Contains("id=\"block-value\"");
    }

    /// <summary>The dashboard shows the pending count, and the nav badge shows it on every admin page.</summary>
    [Test]
    public async Task Dashboard_ShowsPendingCountAndBadge()
    {
        var token = PublicTestPosts.Token();
        var post = await CommentTestData.PublishPostAsync(factory, $"Dashboard post {token}");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Pending, $"Dashboard comment {token}", name: $"Dash {token}");
        using var client = await CreateAdminClientAsync();

        var html = await GetHtmlAsync(client, "/admin");

        await Assert.That(html).Contains("Pending comments");
        await Assert.That(html).Contains("Needs review");
        await Assert.That(html).Contains($"Dash {token}");
        await Assert.That(html).Contains("comments awaiting moderation</span>");
    }

    /// <summary>Anonymous visitors bounced to the login page never get the pending count.</summary>
    [Test]
    public async Task Anonymous_NeverSeesBadge()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync("/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        await Assert.That(html).DoesNotContain("awaiting moderation");
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

        return WebUtility.HtmlDecode(html);
    }
}
