using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests the images chosen in the settings (design 13, T4.25): the favicon is linked from every page, the author avatar
/// is the author's image in a post's JSON-LD, and an image no longer in the library can't be saved. Holds the settings
/// lock and restores the settings it changes.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel(TestConstraints.SiteSettings)]
public partial class SettingsImagesTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The chosen favicon replaces the built-in one, and removing it brings the built-in one back.</summary>
    [Test]
    public async Task Favicon_IsLinkedFromPages()
    {
        var item = await MediaTestFiles.AddAsync(factory, $"favicon-{MediaTestFiles.Token()}.png");
        using var client = IdentityTestHelper.CreateClient(factory);

        string withFavicon;
        await using (await SettingsTestData.ChangeAsync(factory, s => s.FaviconMediaId = item.Id))
        {
            withFavicon = await PublicTestPosts.GetOkAsync(client, "/");
        }

        var withoutFavicon = await PublicTestPosts.GetOkAsync(client, "/");

        await Assert.That(withFavicon).Contains($"<link rel=\"icon\" href=\"{item.Url}\"");
        await Assert.That(withFavicon).DoesNotContain("href=\"favicon.png\"");
        await Assert.That(withoutFavicon).Contains("<link rel=\"icon\" type=\"image/png\" href=\"favicon.png\"");
    }

    /// <summary>With an author name, the avatar is the image of the post's <c>Person</c> author.</summary>
    [Test]
    public async Task AuthorAvatar_IsAuthorImageInJsonLd()
    {
        var token = PublicTestPosts.Token();
        var avatar = await MediaTestFiles.AddAsync(factory, $"avatar-{token}.png");
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Avatar {token}" });
        using var client = IdentityTestHelper.CreateClient(factory);

        string html;
        await using (await SettingsTestData.ChangeAsync(factory, s =>
        {
            s.AuthorName = $"Author {token}";
            s.AuthorAvatarMediaId = avatar.Id;
        }))
        {
            html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);
        }

        using var jsonLd = JsonDocument.Parse(WebUtility.HtmlDecode(JsonLd().Match(html).Groups[1].Value));
        var author = jsonLd.RootElement.GetProperty("author");
        await Assert.That(author.GetProperty("name").GetString()).IsEqualTo($"Author {token}");
        await Assert.That(author.GetProperty("image").GetString()).IsEqualTo($"http://localhost{avatar.Url}");
    }

    /// <summary>
    /// An image deleted from the library while the settings page was open is reported on its field, not as a failed save.
    /// </summary>
    [Test]
    public async Task SaveAsync_ImageNotInLibrary_IsValidationError()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var settings = await settingsService.GetAsync();
        var favicon = settings.FaviconMediaId;
        settings.FaviconMediaId = int.MaxValue;
        settings.DefaultSocialImageMediaId = int.MaxValue - 1;

        var exception = await Assert.ThrowsAsync<ValidationException>(() => settingsService.SaveAsync(settings));

        await Assert.That(exception!.Errors.Select(e => e.PropertyName))
            .IsEquivalentTo([nameof(SiteSettingsDto.FaviconMediaId), nameof(SiteSettingsDto.DefaultSocialImageMediaId)]);
        await Assert.That((await settingsService.GetAsync()).FaviconMediaId).IsEqualTo(favicon);
    }

    [GeneratedRegex("<script type=\"application/ld(?:\\+|&#x2B;)json\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLd();
}
