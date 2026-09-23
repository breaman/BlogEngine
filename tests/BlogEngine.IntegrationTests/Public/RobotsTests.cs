using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Common;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests <c>/robots.txt</c> and the sitewide <c>noindex</c> (design 13, 16, P7, T1.25) in both states of the
/// "discourage search engines" setting. Holds the settings lock and restores the setting afterwards.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.SiteSettings)]
public class RobotsTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>By default crawlers are kept out of the private areas and pointed at the sitemap, and pages are indexable.</summary>
    [Test]
    public async Task Default_DisallowsPrivateAreasAndListsSitemap()
    {
        using var client = IdentityTestHelper.CreateClient(factory);

        using var response = await client.GetAsync(SitePaths.Robots);
        var robots = await response.Content.ReadAsStringAsync();
        var home = await PublicTestPosts.GetOkAsync(client, SitePaths.Home);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("text/plain");
        await Assert.That(robots).Contains("User-agent: *\n");
        await Assert.That(robots).Contains("Disallow: /admin\n");
        await Assert.That(robots).Contains("Disallow: /api\n");
        await Assert.That(robots).Contains("Disallow: /preview\n");
        await Assert.That(robots).Contains("Disallow: /Account\n");
        await Assert.That(robots).DoesNotContain("Disallow: /\n");
        await Assert.That(robots).Contains("Sitemap: http://localhost/sitemap.xml\n");
        await Assert.That(home).DoesNotContain("noindex");
    }

    /// <summary>
    /// Turning "discourage search engines" on disallows everything and adds <c>noindex</c> to every page, on the
    /// next request even though <c>robots.txt</c> was cached; the author's extras are appended.
    /// </summary>
    [Test]
    public async Task DiscourageSearchEngines_DisallowsEverythingAndAddsNoindex()
    {
        var original = await GetSettingsAsync();
        using var client = IdentityTestHelper.CreateClient(factory);
        await client.GetStringAsync(SitePaths.Robots);

        try
        {
            var changed = await GetSettingsAsync();
            changed.DiscourageSearchEngines = true;
            changed.RobotsTxtExtras = "User-agent: GPTBot\r\nDisallow: /";
            await SaveSettingsAsync(changed);

            var robots = await client.GetStringAsync(SitePaths.Robots);
            var home = await PublicTestPosts.GetOkAsync(client, SitePaths.Home);
            var posts = await PublicTestPosts.GetOkAsync(client, PostPaths.Index);

            await Assert.That(robots).StartsWith("User-agent: *\nDisallow: /\n");
            await Assert.That(robots).DoesNotContain("Disallow: /admin");
            await Assert.That(robots).Contains("User-agent: GPTBot\nDisallow: /\n");
            await Assert.That(home).Contains("<meta name=\"robots\" content=\"noindex, nofollow\"");
            await Assert.That(posts).Contains("<meta name=\"robots\" content=\"noindex, nofollow\"");
        }
        finally
        {
            await SaveSettingsAsync(original);
        }

        await Assert.That(await client.GetStringAsync(SitePaths.Robots)).Contains("Disallow: /admin\n");
    }

    private async Task<SiteSettingsDto> GetSettingsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync();
    }

    private async Task SaveSettingsAsync(SiteSettingsDto settings)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SaveAsync(settings);
    }
}
