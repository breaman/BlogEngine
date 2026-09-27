using System.Net;
using System.Net.Http.Json;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Enums;
using BlogEngine.Shared.Security;

using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.IntegrationTests.Api;

/// <summary>
/// Tests the admin page endpoints over HTTP (design 6.7, 7.4, A17, T4.10): every endpoint rejects anonymous callers
/// (401) and non-admins (403); a page created and published through the API is served at <c>/{slug}</c> and linked
/// from the navigation; reserved and taken slugs are 400s; deleting removes the page.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.Users)]
public class AdminPagesApiTests(BlogEngineWebApplicationFactory factory)
{
    private const string PagesApi = "/api/admin/pages";

    /// <summary>Every page endpoint, as (method, path).</summary>
    public static IEnumerable<(string Method, string Path)> Endpoints()
    {
        yield return ("GET", PagesApi);
        yield return ("GET", $"{PagesApi}/1");
        yield return ("POST", PagesApi);
        yield return ("PUT", $"{PagesApi}/1");
        yield return ("POST", $"{PagesApi}/1/publish");
        yield return ("POST", $"{PagesApi}/1/unpublish");
        yield return ("DELETE", $"{PagesApi}/1");
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
    /// The T4.10 "done when": an About page created and published through the API is reachable at <c>/about</c>, appears
    /// in the navigation and the sitemap, and is gone from all three once deleted.
    /// </summary>
    [Test]
    public async Task AboutPage_IsServedAtSlug_AndInNavigation_UntilDeleted()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var marker = PublicTestPosts.Token();

        using var create = await SendAsync(client, token, HttpMethod.Post, PagesApi, new PageEditDto
        {
            Title = "About",
            Slug = "about",
            ContentMarkdown = $"Hello, I write this blog. {marker}",
            ShowInNav = true
        });
        var created = (await create.Content.ReadFromJsonAsync<PageEditDto>())!;
        using var anonymous = IdentityTestHelper.CreateClient(factory);
        using var beforePublish = await anonymous.GetAsync("/about");

        using var publish = await SendAsync(client, token, HttpMethod.Post, $"{PagesApi}/{created.Id}/publish");
        var published = (await publish.Content.ReadFromJsonAsync<PageEditDto>())!;
        var page = await PublicTestPosts.GetOkAsync(anonymous, "/about");
        var home = await PublicTestPosts.GetOkAsync(anonymous, "/");
        var sitemap = await PublicTestPosts.GetOkAsync(anonymous, "/sitemap.xml");

        using var delete = await SendAsync(client, token, HttpMethod.Delete, $"{PagesApi}/{created.Id}");
        using var afterDelete = await anonymous.GetAsync("/about");
        var homeAfterDelete = await PublicTestPosts.GetOkAsync(anonymous, "/");
        using var reload = await client.GetAsync($"{PagesApi}/{created.Id}");

        await Assert.That(create.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(created.Status).IsEqualTo(PostStatus.Draft);
        await Assert.That(beforePublish.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(published.PublicPath).IsEqualTo("/about");
        await Assert.That(page).Contains("<h1 class=\"post-title mb-0\">About</h1>");
        await Assert.That(page).Contains($"Hello, I write this blog. {marker}");
        await Assert.That(page).Contains("<link rel=\"canonical\" href=\"http://localhost/about\"");
        await Assert.That(home).Contains("href=\"/about\"");
        await Assert.That(sitemap).Contains("<loc>http://localhost/about</loc>");
        await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(afterDelete.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(homeAfterDelete).DoesNotContain("href=\"/about\"");
        await Assert.That(reload.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>A typed slug that the site itself uses is a validation error on the slug.</summary>
    [Test]
    [Arguments("posts")]
    [Arguments("admin")]
    [Arguments("search")]
    public async Task Create_ReservedSlug_Returns400(string slug)
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;

        using var response = await SendAsync(client, token, HttpMethod.Post, PagesApi, new PageEditDto { Title = "Reserved", Slug = slug });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(problem!.Errors.Keys).Contains(nameof(PageEditDto.Slug));
    }

    /// <summary>A typed slug another page uses is rejected rather than silently changed; a generated one gets a suffix.</summary>
    [Test]
    public async Task Slugs_TypedDuplicateRejected_GeneratedAvoidsTakenAndReserved()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var slug = $"uses-{PublicTestPosts.Token()}";

        using var first = await SendAsync(client, token, HttpMethod.Post, PagesApi, new PageEditDto { Title = "First", Slug = slug });
        using var duplicate = await SendAsync(client, token, HttpMethod.Post, PagesApi, new PageEditDto { Title = "Second", Slug = slug });
        using var generated = await SendAsync(client, token, HttpMethod.Post, PagesApi, new PageEditDto { Title = slug });
        using var reserved = await SendAsync(client, token, HttpMethod.Post, PagesApi, new PageEditDto { Title = "Archive" });

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(duplicate.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await generated.Content.ReadFromJsonAsync<PageEditDto>())!.Slug).IsEqualTo($"{slug}-2");
        await Assert.That((await reserved.Content.ReadFromJsonAsync<PageEditDto>())!.Slug).StartsWith("archive-");
    }

    /// <summary>Operations on a page that doesn't exist answer 404.</summary>
    [Test]
    public async Task UnknownPage_Returns404()
    {
        var (client, token) = await LoginAdminAsync();
        using var _ = client;
        var missing = $"{PagesApi}/{int.MaxValue}";

        using var get = await client.GetAsync(missing);
        using var put = await SendAsync(client, token, HttpMethod.Put, missing, new PageEditDto { Title = "Nope" });
        using var publish = await SendAsync(client, token, HttpMethod.Post, $"{missing}/publish");
        using var unpublish = await SendAsync(client, token, HttpMethod.Post, $"{missing}/unpublish");
        using var delete = await SendAsync(client, token, HttpMethod.Delete, missing);

        foreach (var response in new[] { get, put, publish, unpublish, delete })
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
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