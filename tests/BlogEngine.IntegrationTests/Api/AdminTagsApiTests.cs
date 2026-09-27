using System.Net;
using System.Net.Http.Json;

using BlogEngine.Data.Common;
using BlogEngine.Data.Models;
using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Security;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests tag management over HTTP (design 6.4, 7.4, O3, T4.22): every endpoint rejects anonymous callers (401) and
/// non-admins (403); merging moves the posts (including trashed ones) and redirects the old tag page and feed; renaming
/// checks collisions and redirects a changed slug; only unused tags can be deleted.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Users)]
public class AdminTagsApiTests(BlogEngineWebApplicationFactory factory)
{
    private const string TagsApi = "/api/admin/tags";

    /// <summary>Every tag management endpoint, as (method, path).</summary>
    public static IEnumerable<(string Method, string Path)> Endpoints()
    {
        yield return ("GET", $"{TagsApi}/all");
        yield return ("PUT", $"{TagsApi}/1");
        yield return ("POST", $"{TagsApi}/1/merge/2");
        yield return ("DELETE", $"{TagsApi}/1");
    }

    /// <summary>Recreates the admin and reader accounts before each test.</summary>
    [Before(Test)]
    public async Task ResetAccountsAsync()
    {
        await IdentityTestHelper.ResetToSingleAdminAsync(factory);
    }

    /// <summary>Anonymous callers get 401, not a login redirect.</summary>
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

    /// <summary>
    /// The T4.22 "done when": merging <c>csharp</c> into <c>c-sharp</c> retags every post (one that had both keeps one
    /// link, one in the trash is retagged too), deletes the old tag, and redirects its page and feed to the target's.
    /// </summary>
    [Test]
    public async Task Merge_MovesPostsAndRedirectsOldTagUrl()
    {
        var token = PublicTestPosts.Token();
        var sourceName = $"csharp{token}";
        var targetName = $"c sharp {token}";
        var onlySource = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Only source {token}", Tags = [sourceName] });
        var onlyTarget = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Only target {token}", Tags = [targetName] });
        var both = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Both {token}", Tags = [sourceName, targetName] });
        var trashed = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Trashed {token}", Tags = [sourceName] });
        await PublicTestPosts.TrashAsync(factory, trashed.Id);

        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;
        var source = await FindTagAsync(client, sourceName);
        var target = await FindTagAsync(client, targetName);
        using var anonymous = IdentityTestHelper.CreateClient(factory);
        var sourcePageBefore = await PublicTestPosts.GetOkAsync(anonymous, $"/tags/{source.Slug}");

        using var merge = await SendAsync(client, antiforgery, HttpMethod.Post, $"{TagsApi}/{source.Id}/merge/{target.Id}");
        var merged = await merge.Content.ReadFromJsonAsync<TagAdminDto>();
        using var oldPage = await anonymous.GetAsync($"/tags/{source.Slug}?page=1");
        using var oldFeed = await anonymous.GetAsync($"/tags/{source.Slug}/feed.xml");
        var targetPage = await PublicTestPosts.GetOkAsync(anonymous, $"/tags/{target.Slug}");
        var tags = await client.GetFromJsonAsync<List<TagAdminDto>>($"{TagsApi}/all");

        await Assert.That(sourcePageBefore).Contains($"Only source {token}");
        await Assert.That(merge.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(merged!.Id).IsEqualTo(target.Id);
        await Assert.That(merged.PostCount).IsEqualTo(3);
        await Assert.That(merged.TrashedPostCount).IsEqualTo(1);
        await Assert.That(oldPage.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(oldPage.Headers.Location!.OriginalString).IsEqualTo($"/tags/{target.Slug}?page=1");
        await Assert.That(oldFeed.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(oldFeed.Headers.Location!.OriginalString).IsEqualTo($"/tags/{target.Slug}/feed.xml");
        await Assert.That(targetPage).Contains($"Only source {token}");
        await Assert.That(targetPage).Contains($"Only target {token}");
        await Assert.That(targetPage).Contains($"Both {token}");
        await Assert.That(tags!.Any(t => t.Id == source.Id)).IsFalse();
        await Assert.That(await TagNamesAsync(both.Id)).IsEquivalentTo([targetName]);
        await Assert.That(await TagNamesAsync(trashed.Id)).IsEquivalentTo([targetName]);
        await Assert.That(await TagNamesAsync(onlySource.Id)).IsEquivalentTo([targetName]);
        await Assert.That(await TagNamesAsync(onlyTarget.Id)).IsEquivalentTo([targetName]);
    }

    /// <summary>A merge that would change posts' tags bumps their row versions, so an editor opened before sees a conflict.</summary>
    [Test]
    public async Task Merge_ChangesRowVersionOfRetaggedPosts()
    {
        var token = PublicTestPosts.Token();
        var post = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Open editor {token}", Tags = [$"old{token}"] });
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Target holder {token}", Tags = [$"new{token}"] });
        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;
        var source = await FindTagAsync(client, $"old{token}");
        var target = await FindTagAsync(client, $"new{token}");

        using var merge = await SendAsync(client, antiforgery, HttpMethod.Post, $"{TagsApi}/{source.Id}/merge/{target.Id}");
        post.Tags = [$"old{token}"];
        using var staleSave = await SendAsync(client, antiforgery, HttpMethod.Put, $"/api/admin/posts/{post.Id}", post);

        await Assert.That(merge.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(staleSave.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    /// <summary>Merging a tag into itself is a 400; an unknown tag or target is a 404.</summary>
    [Test]
    public async Task Merge_InvalidRequests_AreRejected()
    {
        var token = PublicTestPosts.Token();
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Self {token}", Tags = [$"self{token}"] });
        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;
        var tag = await FindTagAsync(client, $"self{token}");

        using var self = await SendAsync(client, antiforgery, HttpMethod.Post, $"{TagsApi}/{tag.Id}/merge/{tag.Id}");
        using var missingTarget = await SendAsync(client, antiforgery, HttpMethod.Post, $"{TagsApi}/{tag.Id}/merge/{int.MaxValue}");
        using var missingSource = await SendAsync(client, antiforgery, HttpMethod.Post, $"{TagsApi}/{int.MaxValue}/merge/{tag.Id}");

        await Assert.That(self.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(missingTarget.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(missingSource.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Renaming derives a new slug from the name, redirects the old tag page to the new one, updates the public pages, and
    /// saves the description shown on the tag page.
    /// </summary>
    [Test]
    public async Task Rename_DerivesSlugAndRedirectsOldPage()
    {
        var token = PublicTestPosts.Token();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Renamed tag post {token}", Tags = [$"before{token}"] });
        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;
        var tag = await FindTagAsync(client, $"before{token}");
        using var anonymous = IdentityTestHelper.CreateClient(factory);
        await PublicTestPosts.GetOkAsync(anonymous, $"/tags/{tag.Slug}");

        using var put = await SendAsync(client, antiforgery, HttpMethod.Put, $"{TagsApi}/{tag.Id}",
            new UpdateTagRequest { Name = $"After {token}", Slug = "", Description = $"About after {token}." });
        var renamed = await put.Content.ReadFromJsonAsync<TagAdminDto>();
        using var oldPage = await anonymous.GetAsync($"/tags/{tag.Slug}");
        var newPage = await PublicTestPosts.GetOkAsync(anonymous, $"/tags/after-{token}");

        await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(renamed!.Name).IsEqualTo($"After {token}");
        await Assert.That(renamed.Slug).IsEqualTo($"after-{token}");
        await Assert.That(oldPage.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(oldPage.Headers.Location!.OriginalString).IsEqualTo($"/tags/after-{token}");
        await Assert.That(newPage).Contains($"After {token}");
        await Assert.That(newPage).Contains($"About after {token}.");
        await Assert.That(newPage).Contains($"Renamed tag post {token}");
    }

    /// <summary>Fixing only the casing keeps the slug, so the tag's URL doesn't move and no redirect is created.</summary>
    [Test]
    public async Task Rename_CasingOnly_KeepsSlug()
    {
        var token = PublicTestPosts.Token();
        await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Casing {token}", Tags = [$"casing{token}"] });
        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;
        var tag = await FindTagAsync(client, $"casing{token}");

        using var put = await SendAsync(client, antiforgery, HttpMethod.Put, $"{TagsApi}/{tag.Id}", new UpdateTagRequest { Name = $"CASING{token}" });
        var renamed = await put.Content.ReadFromJsonAsync<TagAdminDto>();
        using var anonymous = IdentityTestHelper.CreateClient(factory);
        var page = await PublicTestPosts.GetOkAsync(anonymous, $"/tags/{tag.Slug}");

        await Assert.That(renamed!.Name).IsEqualTo($"CASING{token}");
        await Assert.That(renamed.Slug).IsEqualTo(tag.Slug);
        await Assert.That(page).Contains($"CASING{token}");
        await Assert.That(await RedirectExistsAsync($"/tags/{tag.Slug}")).IsFalse();
    }

    /// <summary>A name another tag has (in any casing) is a 409 that suggests merging; a typed slug another tag has is a 400.</summary>
    [Test]
    public async Task Rename_Collisions_AreRejected()
    {
        var token = PublicTestPosts.Token();
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Collide {token}", Tags = [$"first{token}", $"second{token}"] });
        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;
        var first = await FindTagAsync(client, $"first{token}");
        var second = await FindTagAsync(client, $"second{token}");

        using var sameName = await SendAsync(client, antiforgery, HttpMethod.Put, $"{TagsApi}/{first.Id}",
            new UpdateTagRequest { Name = $"SECOND{token}" });
        var conflict = await sameName.Content.ReadFromJsonAsync<ProblemDetails>();
        using var sameSlug = await SendAsync(client, antiforgery, HttpMethod.Put, $"{TagsApi}/{first.Id}",
            new UpdateTagRequest { Name = $"first{token}", Slug = second.Slug });
        var invalid = await sameSlug.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        using var badName = await SendAsync(client, antiforgery, HttpMethod.Put, $"{TagsApi}/{first.Id}", new UpdateTagRequest { Name = "a,b" });

        await Assert.That(sameName.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(conflict!.Detail).Contains("Merge this tag into it instead.");
        await Assert.That(sameSlug.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(invalid!.Errors.Keys).IsEquivalentTo([nameof(UpdateTagRequest.Slug)]);
        await Assert.That(badName.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await FindTagAsync(client, $"first{token}")).Slug).IsEqualTo(first.Slug);
    }

    /// <summary>
    /// A tag no post uses can be deleted; one used by a post, even only by a post in the trash, is a 409 that says so.
    /// </summary>
    [Test]
    public async Task Delete_OnlyUnusedTags()
    {
        var token = PublicTestPosts.Token();
        var post = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Delete tags {token}", Tags = [$"unused{token}"] });
        post.Tags = [];
        await PublicTestPosts.SaveAsync(factory, posts => posts.UpdateAsync(post.Id, post));
        var trashed = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Trash tags {token}", Tags = [$"intrash{token}"] });
        await PublicTestPosts.TrashAsync(factory, trashed.Id);
        await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Live tags {token}", Tags = [$"inuse{token}"] });
        var (client, antiforgery) = await LoginAdminAsync();
        using var _ = client;
        var unused = await FindTagAsync(client, $"unused{token}");
        var inTrash = await FindTagAsync(client, $"intrash{token}");
        var inUse = await FindTagAsync(client, $"inuse{token}");

        using var deleteUnused = await SendAsync(client, antiforgery, HttpMethod.Delete, $"{TagsApi}/{unused.Id}");
        using var deleteInTrash = await SendAsync(client, antiforgery, HttpMethod.Delete, $"{TagsApi}/{inTrash.Id}");
        var trashConflict = await deleteInTrash.Content.ReadFromJsonAsync<ProblemDetails>();
        using var deleteInUse = await SendAsync(client, antiforgery, HttpMethod.Delete, $"{TagsApi}/{inUse.Id}");
        using var deleteAgain = await SendAsync(client, antiforgery, HttpMethod.Delete, $"{TagsApi}/{unused.Id}");

        await Assert.That(unused.IsUnused).IsTrue();
        await Assert.That(inTrash.PostCount).IsEqualTo(0);
        await Assert.That(inTrash.TrashedPostCount).IsEqualTo(1);
        await Assert.That(deleteUnused.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(deleteInTrash.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(trashConflict!.Detail).Contains("1 post in the trash");
        await Assert.That(deleteInUse.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(deleteAgain.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    private async Task<(HttpClient Client, string Token)> LoginAdminAsync()
    {
        var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        return (client, await IdentityTestHelper.GetAntiforgeryTokenAsync(client, "/admin"));
    }

    /// <summary>The tag management row of the tag with this name (matched ignoring case).</summary>
    private static async Task<TagAdminDto> FindTagAsync(HttpClient client, string name)
    {
        var tags = await client.GetFromJsonAsync<List<TagAdminDto>>($"{TagsApi}/all");
        return tags!.Single(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The tag names of a post, including a post in the trash.</summary>
    private async Task<List<string>> TagNamesAsync(int postId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.PostTags
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(pt => pt.PostId == postId)
            .Select(pt => pt.Tag.Name)
            .ToListAsync();
    }

    private async Task<bool> RedirectExistsAsync(string fromPath)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Redirects.AnyAsync(r => r.FromPath == fromPath);
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

    /// <summary>A request with a minimal JSON body for writes, so it reaches authorization rather than failing binding.</summary>
    private static HttpRequestMessage CreateRequest(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "PUT")
        {
            request.Content = JsonContent.Create(new { name = "x" });
        }

        return request;
    }
}