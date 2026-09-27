using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests standalone pages on the public site (design 6.7, 7.1, A17, T4.10): only published pages are served, other
/// spellings of the URL redirect to the canonical one, a renamed page redirects from its old URL, unpublishing takes it
/// out of the navigation at once, and the privacy template renders.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class StandalonePageTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>A draft page is 404 at its URL.</summary>
    [Test]
    public async Task DraftPage_Returns404()
    {
        var page = await SaveAsync(s => s.CreateAsync(new PageEditDto { Title = $"Draft page {PublicTestPosts.Token()}" }));
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(SitePaths.Page(page.Slug!));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>Upper-case and trailing-slash spellings answer 301 to the canonical lowercase path.</summary>
    [Test]
    public async Task NonCanonicalPath_Returns301()
    {
        var page = await PublishAsync(new PageEditDto { Title = $"Canonical page {PublicTestPosts.Token()}" });
        using var client = IdentityTestHelper.CreateClient(factory);

        foreach (var path in new[] { "/" + page.Slug!.ToUpperInvariant(), page.PublicPath + "/" })
        {
            using var response = await client.GetAsync(path);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
            await Assert.That(response.Headers.Location!.ToString()).IsEqualTo(page.PublicPath);
        }
    }

    /// <summary>Renaming a published page records a redirect from its old URL (as for posts, P16).</summary>
    [Test]
    public async Task RenamedPage_OldUrlRedirects()
    {
        var token = PublicTestPosts.Token();
        var page = await PublishAsync(new PageEditDto { Title = $"Old name {token}", Slug = $"old-name-{token}" });
        var oldPath = page.PublicPath!;
        page.Slug = $"new-name-{token}";
        var renamed = await SaveAsync(s => s.UpdateAsync(page.Id, page));
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(oldPath);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MovedPermanently);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo(renamed.PublicPath);
        await Assert.That(await PublicTestPosts.GetOkAsync(client, renamed.PublicPath!)).Contains($"Old name {token}");
    }

    /// <summary>Unpublishing evicts the cached page and navigation, so both change on the next request.</summary>
    [Test]
    public async Task UnpublishedPage_LeavesNavigation_AndReturns404()
    {
        var token = PublicTestPosts.Token();
        var page = await PublishAsync(new PageEditDto { Title = $"Nav page {token}", ShowInNav = true, NavOrder = 5 });
        using var client = IdentityTestHelper.CreateClient(factory);
        var before = await PublicTestPosts.GetOkAsync(client, "/tags");

        await SaveAsync(s => s.UnpublishAsync(page.Id));
        var after = await PublicTestPosts.GetOkAsync(client, "/tags");
        using var response = await client.GetAsync(page.PublicPath!);

        await Assert.That(before).Contains($">Nav page {token}</a>");
        await Assert.That(after).DoesNotContain($"Nav page {token}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    /// <summary>A page hidden from the navigation is still served, just not linked.</summary>
    [Test]
    public async Task HiddenPage_IsServedButNotInNavigation()
    {
        var token = PublicTestPosts.Token();
        var page = await PublishAsync(new PageEditDto { Title = $"Unlisted {token}", ContentMarkdown = "Secret-ish." });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, page.PublicPath!);

        await Assert.That(html).Contains("Secret-ish.");
        await Assert.That(html).DoesNotContain($"href=\"{page.PublicPath}\"");
    }

    /// <summary>
    /// The privacy template (design 12.3) renders as a page; with four sections it gets a table of contents whose links,
    /// like the heading anchors, include the page's path (the site's &lt;base href="/"&gt; would send a bare #id home).
    /// </summary>
    [Test]
    public async Task PrivacyTemplate_RendersWithTableOfContents()
    {
        var template = PageTemplates.Privacy;
        var slug = $"privacy-{PublicTestPosts.Token()}";
        var page = await PublishAsync(new PageEditDto { Title = template.Title, Slug = slug, Summary = template.Summary, ContentMarkdown = template.Markdown });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, page.PublicPath!);

        await Assert.That(html).Contains("no third-party analytics");
        await Assert.That(html).Contains("class=\"post-toc");
        await Assert.That(html).Contains($"<a href=\"/{slug}#comments\">Comments</a>");
        await Assert.That(html).Contains($"<h2 id=\"comments\"><a href=\"/{slug}#comments\" class=\"heading-anchor\">Comments</a></h2>");
        await Assert.That(html).Contains($"<meta name=\"description\" content=\"{template.Summary}\"");
    }

    private async Task<PageEditDto> PublishAsync(PageEditDto page)
    {
        var draft = await SaveAsync(s => s.CreateAsync(page));
        return await SaveAsync(s => s.PublishAsync(draft.Id));
    }

    private async Task<PageEditDto> SaveAsync(Func<IPageAdminService, Task<PageSaveResult>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPageAdminService>()) is PageSaved saved
            ? saved.Page
            : throw new InvalidOperationException("Expected the page to save.");
    }
}