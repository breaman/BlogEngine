using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Layout tests that change the site settings: the tagline, social links and title show on public pages as
/// soon as the settings are saved. Holds the settings lock and restores the original settings.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.SiteSettings)]
public class PublicLayoutSettingsTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The header shows the title and tagline, and the footer the author and social links.</summary>
    [Test]
    public async Task Layout_ShowsIdentityAndSocialLinks()
    {
        var token = PublicTestPosts.Token();
        var original = await GetSettingsAsync();
        using var client = IdentityTestHelper.CreateClient(factory);
        await PublicTestPosts.GetOkAsync(client, "/");

        try
        {
            var changed = await GetSettingsAsync();
            changed.SiteTitle = $"Blog {token}";
            changed.Tagline = $"Tagline {token}";
            changed.Description = $"Description {token}";
            changed.AuthorName = $"Author {token}";
            changed.SocialLinks = [new SocialLinkDto { Network = "GitHub", Url = $"https://github.com/{token}" }];
            await SaveSettingsAsync(changed);

            var html = await PublicTestPosts.GetOkAsync(client, "/");

            await Assert.That(html).Contains($"<title>Blog {token}</title>");
            await Assert.That(html).Contains($"Tagline {token}");
            await Assert.That(html).Contains($"<meta name=\"description\" content=\"Description {token}\"");
            await Assert.That(html).Contains($"Author {token}</p>");
            await Assert.That(html).Contains($"href=\"https://github.com/{token}\"");
            await Assert.That(html).Contains("bi-github");
        }
        finally
        {
            await SaveSettingsAsync(original);
        }
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
