using System.Net;
using System.Net.Http.Json;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Security;

using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests the admin post and tag endpoints over HTTP (design 7.4, T1.7): every endpoint rejects anonymous
/// callers (401) and non-admins (403), maps service outcomes to 200/201/204/404/400, and answers a stale
/// <c>RowVersion</c> with 409.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Users)]
public class AdminPostsApiTests(BlogEngineWebApplicationFactory factory)
{
    private const string PostsApi = "/api/admin/posts";

    /// <summary>Every post and tag endpoint, as (method, path).</summary>
    public static IEnumerable<(string Method, string Path)> Endpoints()
    {
        yield return ("GET", PostsApi);
        yield return ("GET", $"{PostsApi}/1");
        yield return ("POST", PostsApi);
        yield return ("PUT", $"{PostsApi}/1");
        yield return ("POST", $"{PostsApi}/1/autosave");
        yield return ("POST", $"{PostsApi}/1/publish");
        yield return ("POST", $"{PostsApi}/1/unpublish");
        yield return ("DELETE", $"{PostsApi}/1");
        yield return ("POST", $"{PostsApi}/slug-check");
        yield return ("GET", "/api/admin/tags?search=c");
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

    /// <summary>Creating needs the antiforgery token like every other mutation.</summary>
    [Test]
    public async Task Create_WithoutToken_IsRejected()
    {
        using var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);

        using var response = await client.PostAsJsonAsync(PostsApi, new PostEditDto { Title = "No token" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>POST creates a draft: 201 with a Location that GET resolves.</summary>
    [Test]
    public async Task Create_Returns201_AndGetReturnsPost()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, PostsApi, new PostEditDto { Title = $"Api create {Unique()}", Tags = ["Api"] });
        var created = await response.Content.ReadFromJsonAsync<PostEditDto>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(response.Headers.Location!.ToString()).EndsWith($"{PostsApi}/{created!.Id}");
        var loaded = await client.GetFromJsonAsync<PostEditDto>($"{PostsApi}/{created.Id}");
        await Assert.That(loaded!.Title).IsEqualTo(created.Title);
        await Assert.That(loaded.Tags).IsEquivalentTo(["Api"]);
    }

    /// <summary>Invalid input comes back as a validation problem keyed by field.</summary>
    [Test]
    public async Task Create_Invalid_Returns400WithFieldErrors()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, PostsApi, new PostEditDto { Title = "", Slug = "Bad Slug" });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(problem!.Errors.Keys).IsEquivalentTo([nameof(PostEditDto.Title), nameof(PostEditDto.Slug)]);
    }

    /// <summary>Unknown posts are 404 for reads and every write.</summary>
    [Test]
    public async Task UnknownPost_Returns404()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var missing = $"{PostsApi}/{int.MaxValue}";
        var body = new PostEditDto { Title = "x", RowVersion = [1] };

        using var get = await client.GetAsync(missing);
        using var put = await SendAsync(client, token, HttpMethod.Put, missing, body);
        using var autosave = await SendAsync(client, token, HttpMethod.Post, $"{missing}/autosave", body);
        using var publish = await SendAsync(client, token, HttpMethod.Post, $"{missing}/publish", new PublishPostRequest());
        using var unpublish = await SendAsync(client, token, HttpMethod.Post, $"{missing}/unpublish", new UnpublishPostRequest());
        using var delete = await SendAsync(client, token, HttpMethod.Delete, missing);

        foreach (var response in new[] { get, put, autosave, publish, unpublish, delete })
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
    }

    /// <summary>PUT saves with the current RowVersion, then the old RowVersion is a 409 conflict.</summary>
    [Test]
    public async Task Update_Returns200_ThenStaleVersionReturns409()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var post = await CreatePostAsync(client, token);
        var stale = post.RowVersion;
        post.ContentMarkdown = "Updated over HTTP.";

        using var ok = await SendAsync(client, token, HttpMethod.Put, $"{PostsApi}/{post.Id}", post);
        post.RowVersion = stale;
        using var conflict = await SendAsync(client, token, HttpMethod.Put, $"{PostsApi}/{post.Id}", post);

        await Assert.That(ok.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ok.Content.ReadFromJsonAsync<PostEditDto>())!.ContentMarkdown).IsEqualTo("Updated over HTTP.");
        await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    /// <summary>Autosave returns 200, and 409 when the post changed since it was loaded.</summary>
    [Test]
    public async Task Autosave_Returns200_ThenStaleVersionReturns409()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var post = await CreatePostAsync(client, token);
        var stale = post.RowVersion;
        post.ContentMarkdown = "Autosaved over HTTP.";

        using var ok = await SendAsync(client, token, HttpMethod.Post, $"{PostsApi}/{post.Id}/autosave", post);
        post.RowVersion = stale;
        using var conflict = await SendAsync(client, token, HttpMethod.Post, $"{PostsApi}/{post.Id}/autosave", post);

        await Assert.That(ok.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    /// <summary>Publish and unpublish return the post, and 409 for a stale RowVersion.</summary>
    [Test]
    public async Task PublishAndUnpublish_Return200_AndStaleVersionReturns409()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var post = await CreatePostAsync(client, token);

        using var publish = await SendAsync(client, token, HttpMethod.Post, $"{PostsApi}/{post.Id}/publish",
            new PublishPostRequest { RowVersion = post.RowVersion });
        var published = await publish.Content.ReadFromJsonAsync<PostEditDto>();
        using var staleUnpublish = await SendAsync(client, token, HttpMethod.Post, $"{PostsApi}/{post.Id}/unpublish",
            new UnpublishPostRequest { RowVersion = post.RowVersion });
        using var unpublish = await SendAsync(client, token, HttpMethod.Post, $"{PostsApi}/{post.Id}/unpublish",
            new UnpublishPostRequest { RowVersion = published!.RowVersion });
        using var stalePublish = await SendAsync(client, token, HttpMethod.Post, $"{PostsApi}/{post.Id}/publish",
            new PublishPostRequest { RowVersion = published.RowVersion });

        await Assert.That(publish.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(published.Status).IsEqualTo(PostStatus.Published);
        await Assert.That(staleUnpublish.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(unpublish.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await unpublish.Content.ReadFromJsonAsync<PostEditDto>())!.Status).IsEqualTo(PostStatus.Draft);
        await Assert.That(stalePublish.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    /// <summary>Unpublish accepts an empty body (no concurrency check).</summary>
    [Test]
    public async Task Unpublish_WithoutBody_Returns200()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var post = await CreatePostAsync(client, token);

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{PostsApi}/{post.Id}/unpublish");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    /// <summary>A future publish date is a 400 until scheduling exists.</summary>
    [Test]
    public async Task Publish_FutureDate_Returns400()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var post = await CreatePostAsync(client, token);

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{PostsApi}/{post.Id}/publish",
            new PublishPostRequest { PublishOn = DateTimeOffset.UtcNow.AddHours(2) });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>DELETE moves the post to the trash: 204, then the post is gone from the API.</summary>
    [Test]
    public async Task Delete_Returns204_ThenPostIsGone()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var post = await CreatePostAsync(client, token);

        using var delete = await SendAsync(client, token, HttpMethod.Delete, $"{PostsApi}/{post.Id}");
        using var get = await client.GetAsync($"{PostsApi}/{post.Id}");

        await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>The list endpoint binds its filters from the query string.</summary>
    [Test]
    public async Task List_FiltersFromQueryString()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var tokenText = Unique();
        var post = await CreatePostAsync(client, token, $"Listed {tokenText}");

        var result = await client.GetFromJsonAsync<PagedResult<PostSummaryDto>>(
            $"{PostsApi}?status=Draft&search={tokenText}&page=1&pageSize=5");

        await Assert.That(result!.TotalCount).IsEqualTo(1);
        await Assert.That(result.Items.Single().Id).IsEqualTo(post.Id);
    }

    /// <summary>The slug check reports a taken slug and the suffix a save would use.</summary>
    [Test]
    public async Task SlugCheck_ReportsTakenSlug()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var post = await CreatePostAsync(client, token);

        using var response = await SendAsync(client, token, HttpMethod.Post, $"{PostsApi}/slug-check", new SlugCheckRequest { Slug = post.Slug });
        var result = await response.Content.ReadFromJsonAsync<SlugCheckResult>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(result!.IsAvailable).IsFalse();
        await Assert.That(result.Suggestion).IsEqualTo(post.Slug + "-2");
    }

    /// <summary>Tag autocomplete matches case-insensitively and returns the original casing.</summary>
    [Test]
    public async Task Tags_SearchIsCaseInsensitive()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var tagName = $"Blazor{Unique()}";
        await CreatePostAsync(client, token, tags: [tagName]);

        var tags = await client.GetFromJsonAsync<List<TagDto>>($"/api/admin/tags?search={tagName.ToLowerInvariant()}");

        await Assert.That(tags!.Select(t => t.Name)).IsEquivalentTo([tagName]);
    }

    private static string Unique()
    {
        return Guid.NewGuid().ToString("N")[..10];
    }

    private async Task<(HttpClient Client, string Token)> LoginAdminAsync()
    {
        var client = IdentityTestHelper.CreateClient(factory);
        await IdentityTestHelper.LoginAsync(client, IdentityTestHelper.AdminEmail);
        return (client, await IdentityTestHelper.GetAntiforgeryTokenAsync(client, "/admin"));
    }

    private static async Task<PostEditDto> CreatePostAsync(HttpClient client, string token, string? title = null, List<string>? tags = null)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, PostsApi,
            new PostEditDto { Title = title ?? $"Api post {Unique()}", Tags = tags ?? [] });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PostEditDto>())!;
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
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { title = "x" });
        }

        return request;
    }
}
