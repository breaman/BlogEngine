using System.Net;

using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Public;

/// <summary>
/// Tests cover images (A15, T4.5) and per-post SEO overrides (A16, T4.6): the cover shows on the post page and counts
/// as media usage, and the meta title, meta description and social image end up in the rendered <c>&lt;head&gt;</c>.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class PostImagesAndSeoTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>The cover renders above the content, with its size and alt text, and is the default social image.</summary>
    [Test]
    public async Task Cover_ShowsOnPostPage_AndIsDefaultSocialImage()
    {
        var marker = MediaTestFiles.Token();
        var cover = await AddImageAsync($"cover-{marker}.png", $"Cover {marker}");
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Covered {marker}",
            ContentMarkdown = "Body.",
            CoverMediaId = cover.Id
        });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(post.CoverImage).IsNotNull();
        await Assert.That(post.CoverImage!.Url).IsEqualTo(cover.Url);
        await Assert.That(html).Contains("class=\"post-cover");
        await Assert.That(html).Contains($"src=\"{WebUtility.HtmlEncode(cover.Url)}\" alt=\"Cover {marker}\" width=\"{cover.Width}\" height=\"{cover.Height}\"");
        await Assert.That(html).Contains($"<meta property=\"og:image\" content=\"http://localhost{WebUtility.HtmlEncode(cover.Url)}\"");
        await Assert.That(html).Contains("<meta name=\"twitter:card\" content=\"summary_large_image\"");
    }

    /// <summary>A post without a cover or social image has no social image tags (while no default image is set).</summary>
    [Test]
    [NotInParallel(TestConstraints.SiteSettings)]
    public async Task NoImages_NoSocialImageTags()
    {
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto { Title = $"Plain {MediaTestFiles.Token()}", ContentMarkdown = "Body." });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).DoesNotContain("og:image");
        await Assert.That(html).DoesNotContain("class=\"post-cover");
    }

    /// <summary>The cover counts as a use of the image, so it isn't "unused" and can't be deleted silently (A15).</summary>
    [Test]
    public async Task Cover_IsTrackedInMediaUsage()
    {
        var marker = MediaTestFiles.Token();
        var cover = await AddImageAsync($"usage-cover-{marker}.png", "Cover");
        var post = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Cover usage {marker}", CoverMediaId = cover.Id });

        var used = await LoadMediaAsync(cover.Id);
        var unused = await UnusedIdsAsync(marker);

        post.CoverMediaId = null;
        await PublicTestPosts.SaveAsync(factory, posts => posts.UpdateAsync(post.Id, post));
        var freed = await LoadMediaAsync(cover.Id);

        await Assert.That(used.UsageCount).IsEqualTo(1);
        await Assert.That(used.UsedIn.Single().PostId).IsEqualTo(post.Id);
        await Assert.That(unused).DoesNotContain(cover.Id);
        await Assert.That(freed.UsageCount).IsEqualTo(0);
    }

    /// <summary>The meta title, meta description and social image override the defaults in the rendered head (A16).</summary>
    [Test]
    public async Task SeoOverrides_AppearInHead()
    {
        var marker = MediaTestFiles.Token();
        var cover = await AddImageAsync($"seo-cover-{marker}.png", "Cover");
        var social = await AddImageAsync($"seo-social-{marker}.png", $"Social {marker}");
        var post = await PublicTestPosts.PublishAsync(factory, new PostEditDto
        {
            Title = $"Original title {marker}",
            Summary = "The summary.",
            ContentMarkdown = "Body.",
            MetaTitle = $"Meta title {marker}",
            MetaDescription = $"Meta description {marker}",
            CoverMediaId = cover.Id,
            SocialImageMediaId = social.Id
        });
        using var client = IdentityTestHelper.CreateClient(factory);

        var html = await PublicTestPosts.GetOkAsync(client, post.PublicPath!);

        await Assert.That(html).Contains($"<title>Meta title {marker} | ");
        await Assert.That(html).Contains($"<meta name=\"description\" content=\"Meta description {marker}\"");
        await Assert.That(html).Contains($"<meta property=\"og:image\" content=\"http://localhost{WebUtility.HtmlEncode(social.Url)}\"");
        await Assert.That(html).Contains($"<meta property=\"og:image:alt\" content=\"Social {marker}\"");
        await Assert.That(html).DoesNotContain($"og:image\" content=\"http://localhost{WebUtility.HtmlEncode(cover.Url)}\"");
        // The page itself keeps the post's real title.
        await Assert.That(html).Contains($"Original title {marker}</h1>");
    }

    /// <summary>A cover that isn't in the library is a validation error on its field, and nothing is saved.</summary>
    [Test]
    public async Task UnknownCover_IsRejected()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostAdminService>();

        var result = await posts.CreateAsync(new PostEditDto { Title = $"Bad cover {MediaTestFiles.Token()}", CoverMediaId = int.MaxValue });

        await Assert.That(result).IsTypeOf<PostInvalid>();
        await Assert.That(((PostInvalid)result).Errors.Keys).IsEquivalentTo([nameof(PostEditDto.CoverMediaId)]);
    }

    /// <summary>Deleting a library image that is a post's social image clears the reference (it can't cascade).</summary>
    [Test]
    public async Task DeletingSocialImage_ClearsReference()
    {
        var marker = MediaTestFiles.Token();
        var social = await AddImageAsync($"gone-{marker}.png", "Social");
        var post = await PublicTestPosts.CreateDraftAsync(factory, new PostEditDto { Title = $"Social gone {marker}", SocialImageMediaId = social.Id });

        await using var scope = factory.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaService>();
        var withoutForce = await media.DeleteAsync(social.Id, force: false);
        var forced = await media.DeleteAsync(social.Id, force: true);
        var reloaded = await scope.ServiceProvider.GetRequiredService<IPostAdminService>().GetPostAsync(post.Id);

        await Assert.That(withoutForce).IsTypeOf<MediaInUse>();
        await Assert.That(forced).IsTypeOf<MediaDeleted>();
        await Assert.That(reloaded!.SocialImageMediaId).IsNull();
        await Assert.That(reloaded.SocialImage).IsNull();
    }

    /// <summary>Uploads a PNG and gives it alt text.</summary>
    private async Task<MediaItemDto> AddImageAsync(string fileName, string altText)
    {
        var item = await MediaTestFiles.AddAsync(factory, fileName, MediaTestFiles.Png(120, 60));

        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediaService>()
            .UpdateAsync(item.Id, new MediaUpdateRequest { AltText = altText });

        return ((MediaSaved)result).Item;
    }

    private async Task<MediaItemDto> LoadMediaAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IMediaService>().GetMediaItemAsync(id))!;
    }

    private async Task<List<int>> UnusedIdsAsync(string search)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediaService>()
            .GetMediaAsync(new MediaListQuery { Search = search, Unused = true, PageSize = MediaListQuery.MaxPageSize });
        return [.. result.Items.Select(i => i.Id)];
    }
}