using System.Net;
using System.Net.Http.Json;

using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Security;
using BlogEngine.Shared.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests the posts trash over HTTP (design 6.8, 7.4, O6, T4.23): the Trash tab lists only trashed posts, restoring makes a
/// post a draft again, and deleting permanently (one post, or emptying the trash) removes the post with its tag links,
/// comments, revisions, media usage, preview links and the redirects that led to it.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Users)]
public class TrashTests(BlogEngineWebApplicationFactory factory)
{
    private const string PostsApi = "/api/admin/posts";

    /// <summary>Every trash endpoint, as (method, path).</summary>
    public static IEnumerable<(string Method, string Path)> Endpoints()
    {
        yield return ("POST", $"{PostsApi}/1/restore");
        yield return ("DELETE", $"{PostsApi}/1/permanent");
        yield return ("POST", $"{PostsApi}/empty-trash");
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

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    /// <summary>Signed-in users without the Admin role get 403.</summary>
    [Test]
    [MethodDataSource(nameof(Endpoints))]
    public async Task Endpoint_NonAdmin_Returns403(string method, string path)
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.ReaderEmail);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    /// <summary>The Trash tab lists trashed posts with their tags and trash date; the other tabs never do.</summary>
    [Test]
    public async Task TrashTab_ListsOnlyTrashedPosts()
    {
        var token = PublicTestPosts.Token();
        var trashed = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Binned {token}", Tags = [$"bin{token}"] });
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Kept {token}" });
        await PublicTestPosts.TrashAsync(factory, trashed.Id);
        var (client, _) = await LoginAdminAsync();
        using var __ = client;

        var trash = await client.GetFromJsonAsync<PagedResult<PostSummaryDto>>($"{PostsApi}?status=Trash&search={token}&page=1&pageSize=20");
        var all = await client.GetFromJsonAsync<PagedResult<PostSummaryDto>>($"{PostsApi}?status=All&search={token}&page=1&pageSize=20");
        var published = await client.GetFromJsonAsync<PagedResult<PostSummaryDto>>($"{PostsApi}?status=Published&search={token}&page=1&pageSize=20");

        var row = trash!.Items.Single();
        await Assert.That(row.Id).IsEqualTo(trashed.Id);
        await Assert.That(row.DeletedOn).IsNotNull();
        await Assert.That(row.Tags).IsEquivalentTo([$"bin{token}"]);
        await Assert.That(row.PublicPath).IsNull();
        await Assert.That(all!.Items.Select(p => p.Title)).IsEquivalentTo([$"Kept {token}"]);
        await Assert.That(published!.Items).IsEmpty();
    }

    /// <summary>
    /// Restoring makes a published post a draft (so nothing reappears on the site by surprise) that keeps its publish
    /// date, so publishing it again brings back the same URL. A post that isn't in the trash can't be restored.
    /// </summary>
    [Test]
    public async Task Restore_ReturnsDraftThatRepublishesAtSameUrl()
    {
        var token = PublicTestPosts.Token();
        var published = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Comeback {token}" },
            PublicTestPosts.Noon(PublicTestPosts.NextYear(), 3, 4));
        await PublicTestPosts.TrashAsync(factory, published.Id);
        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;
        using var anonymous = IdentityTestHelper.CreateClient(factory);

        using var restore = await SendAsync(client, antiforgery, HttpMethod.Post, $"{PostsApi}/{published.Id}/restore");
        var restored = await restore.Content.ReadFromJsonAsync<PostEditDto>();
        using var whileDraft = await anonymous.GetAsync(published.PublicPath);
        using var restoreAgain = await SendAsync(client, antiforgery, HttpMethod.Post, $"{PostsApi}/{published.Id}/restore");
        var republished = await PublicTestPosts.SaveAsync(factory, posts => posts.PublishAsync(published.Id, new PublishPostRequest()));

        await Assert.That(restore.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(restored!.Status).IsEqualTo(PostStatus.Draft);
        await Assert.That(restored.PublishedOn).IsEqualTo(published.PublishedOn);
        await Assert.That(whileDraft.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(restoreAgain.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(republished.PublicPath).IsEqualTo(published.PublicPath);
        await PublicTestPosts.GetOkAsync(anonymous, published.PublicPath!);
    }

    /// <summary>A scheduled post comes back as a plain draft: it forgets its date, as unscheduling does.</summary>
    [Test]
    public async Task Restore_ScheduledPost_ForgetsDate()
    {
        var token = PublicTestPosts.Token();
        var scheduled = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Later {token}" }, DateTimeOffset.UtcNow.AddDays(30));
        await PublicTestPosts.TrashAsync(factory, scheduled.Id);

        var restored = await PublicTestPosts.SaveAsync(factory, posts => posts.RestoreAsync(scheduled.Id));

        await Assert.That(restored.Status).IsEqualTo(PostStatus.Draft);
        await Assert.That(restored.PublishedOn).IsNull();
        await Assert.That(restored.PublishedDateLocal).IsNull();
    }

    /// <summary>
    /// Deleting permanently removes the post and everything that hangs off it (design 6.8), including a reply to a
    /// comment, and the redirects that led to its URL; the tag and the media item stay. Only posts in the trash qualify.
    /// </summary>
    [Test]
    public async Task DeletePermanently_RemovesPostAndRelatedRows()
    {
        var token = PublicTestPosts.Token();
        var item = await MediaTestFiles.AddAsync(factory, $"purged-{token}.png");
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Purged {token}",
            Slug = $"purged-{token}",
            Tags = [$"purge{token}"],
            ContentMarkdown = $"![Image]({item.Path})"
        });
        post.Slug = $"purged-renamed-{token}";
        post = await PublicTestPosts.SaveAsync(factory, posts => posts.UpdateAsync(post.Id, post));
        var comment = await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "Top level.");
        await CommentTestData.AddAsync(factory, post.Id, CommentStatus.Approved, "A reply.", parentId: comment.Id);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPreviewLinkService>().CreateAsync(post.Id, new CreatePreviewLinkRequest());
        }

        var live = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Not trashed {token}" });
        await PublicTestPosts.TrashAsync(factory, post.Id);
        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;

        using var notInTrash = await SendAsync(client, antiforgery, HttpMethod.Delete, $"{PostsApi}/{live.Id}/permanent");
        using var delete = await SendAsync(client, antiforgery, HttpMethod.Delete, $"{PostsApi}/{post.Id}/permanent");
        using var deleteAgain = await SendAsync(client, antiforgery, HttpMethod.Delete, $"{PostsApi}/{post.Id}/permanent");

        await Assert.That(notInTrash.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(deleteAgain.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        await using var check = factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await db.Posts.IgnoreQueryFilters().AnyAsync(p => p.Id == post.Id)).IsFalse();
        await Assert.That(await db.Posts.AnyAsync(p => p.Id == live.Id)).IsTrue();
        await Assert.That(await db.PostTags.IgnoreQueryFilters().CountAsync(pt => pt.PostId == post.Id)).IsEqualTo(0);
        await Assert.That(await db.Comments.IgnoreQueryFilters().CountAsync(c => c.PostId == post.Id)).IsEqualTo(0);
        await Assert.That(await db.PostRevisions.IgnoreQueryFilters().CountAsync(r => r.PostId == post.Id)).IsEqualTo(0);
        await Assert.That(await db.PostMedia.IgnoreQueryFilters().CountAsync(pm => pm.PostId == post.Id)).IsEqualTo(0);
        await Assert.That(await db.PreviewTokens.IgnoreQueryFilters().CountAsync(t => t.PostId == post.Id)).IsEqualTo(0);
        await Assert.That(await db.Redirects.CountAsync(r => r.ToPath == post.PublicPath)).IsEqualTo(0);
        await Assert.That(await db.Tags.AnyAsync(t => t.Name == $"purge{token}")).IsTrue();
        await Assert.That(await db.MediaItems.AnyAsync(m => m.Id == item.Id)).IsTrue();
    }

    /// <summary>
    /// Emptying the trash deletes every trashed post and leaves the rest. Runs alone: it purges the trash of the whole
    /// shared database, which other tests' trashed posts are part of.
    /// </summary>
    [Test]
    [NotInParallel]
    public async Task EmptyTrash_DeletesEveryTrashedPost()
    {
        var token = PublicTestPosts.Token();
        var first = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"First bin {token}" });
        var second = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Second bin {token}" });
        var kept = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Kept bin {token}" });
        await CommentTestData.AddAsync(factory, second.Id, CommentStatus.Pending, "On a post about to go.");
        await PublicTestPosts.TrashAsync(factory, first.Id);
        await PublicTestPosts.TrashAsync(factory, second.Id);
        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;

        using var empty = await SendAsync(client, antiforgery, HttpMethod.Post, $"{PostsApi}/empty-trash");
        var result = await empty.Content.ReadFromJsonAsync<EmptyTrashResponse>();
        var trash = await client.GetFromJsonAsync<PagedResult<PostSummaryDto>>($"{PostsApi}?status=Trash&page=1&pageSize=20");

        await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(result!.Deleted).IsGreaterThanOrEqualTo(2);
        await Assert.That(trash!.TotalCount).IsEqualTo(0);

        await using var check = factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Assert.That(await db.Posts.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(p => p.Id == first.Id || p.Id == second.Id)).IsFalse();
        await Assert.That(await db.Posts.AnyAsync(p => p.Id == kept.Id)).IsTrue();
    }

    private async Task<(HttpClient Client, string Token)> LoginAdminAsync()
    {
        var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        return (client, await IdentityTestHelper.GetAntiforgeryTokenAsync(client, "/admin"));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(AntiforgeryHeaders.RequestToken, token);
        return client.SendAsync(request);
    }
}